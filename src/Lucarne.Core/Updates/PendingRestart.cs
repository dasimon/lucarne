namespace Lucarne.Core.Updates;

/// <summary>
/// The executable an installed update starts once this instance is going: launched at most once,
/// by whichever of the two callers comes first — the application's <c>Exit</c> event, or the
/// shutdown watchdog when the UI thread is stuck and that event will never come.
/// </summary>
/// <remarks>
/// The launch runs under a lock, so the caller that comes second waits for it to finish. On
/// 2026-11-11, starting the freshly replaced executable took about fifteen seconds (Defender's
/// first look at a new file, most likely); the watchdog took that for a hang, found the restart
/// already taken, and ended the process in the middle of it — and nothing started.
/// </remarks>
public sealed class PendingRestart
{
    private readonly object _gate = new();
    private string? _path;

    /// <summary>Sets the executable to start, replacing any set before.</summary>
    /// <returns>True when nothing was scheduled until now.</returns>
    public bool Schedule(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        lock (_gate)
        {
            var first = _path is null;
            _path = path;
            return first;
        }
    }

    /// <summary>
    /// Runs <paramref name="launch"/> on the scheduled executable, if one is still pending, and
    /// returns once it has run — or once the launch another caller started has finished.
    /// </summary>
    public void Start(Action<string> launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        lock (_gate)
        {
            if (_path is not { } path)
            {
                return;
            }

            _path = null;
            launch(path);
        }
    }
}
