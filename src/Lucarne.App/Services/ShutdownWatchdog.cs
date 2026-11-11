using System.Diagnostics;
using System.Windows.Threading;
using Lucarne.Core.Sessions;

namespace Lucarne.App.Services;

/// <summary>
/// Ends the process when the UI thread stops answering during a shutdown, after running
/// <c>beforeExit</c> — the update's restart, when one is scheduled.
/// </summary>
/// <remarks>
/// A Remote Desktop control can deadlock inside its own teardown, on the UI thread (2026-11-10:
/// an update's restart, then one shutdown out of five). Nothing on that thread can time it out, and
/// the window then stays frozen until someone ends it by hand — an update never restarts. This
/// timer runs on the thread pool: once a second it asks the UI thread to answer, and after
/// <see cref="ClosePlan.UnresponsiveSeconds"/> of silence it gives up on the process. Started with
/// the shutdown and never stopped: a process that takes that long to go once its window has
/// closed is stuck as well.
/// </remarks>
internal sealed class ShutdownWatchdog : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private readonly Dispatcher _dispatcher;
    private readonly Action _beforeExit;
    private readonly Stopwatch _sinceAnswer = Stopwatch.StartNew();
    private readonly System.Threading.Timer _timer;
    private int _fired;

    public ShutdownWatchdog(Dispatcher dispatcher, Action beforeExit)
    {
        _dispatcher = dispatcher;
        _beforeExit = beforeExit;
        _timer = new System.Threading.Timer(Tick, null, Period, Period);
        ProbeLog.Write("close", $"Shutdown watchdog armed: {ClosePlan.UnresponsiveSeconds}s of silence ends the process");
    }

    private void Tick(object? state)
    {
        TimeSpan silent;
        lock (_sinceAnswer)
        {
            silent = _sinceAnswer.Elapsed;
        }

        if (!ClosePlan.IsHung(silent))
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                lock (_sinceAnswer)
                {
                    _sinceAnswer.Restart();
                }
            });
            return;
        }

        if (Interlocked.Exchange(ref _fired, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
        ProbeLog.Write("close", $"UI thread silent for {silent.TotalSeconds:F0}s during shutdown; ending the process");
        try
        {
            _beforeExit();
        }
        catch (Exception ex)
        {
            ProbeLog.Write("close", $"Before ending the process: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
        }

        // TerminateProcess, not Environment.Exit: an orderly exit would wait on the very thread
        // that is stuck.
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>Disarms the watchdog. The shell never does: it watches until the process is gone.</summary>
    public void Dispose() => _timer.Dispose();
}
