using Lucarne.Core.Updates;

namespace Lucarne.Core.Tests.Updates;

/// <summary>The restart an update schedules: started once, and never cut short by a second caller.</summary>
public sealed class PendingRestartTests
{
    [Fact]
    public void Nothing_scheduled_launches_nothing()
    {
        var restart = new PendingRestart();
        var launched = new List<string>();

        restart.Start(launched.Add);

        Assert.Empty(launched);
    }

    [Fact]
    public void A_scheduled_restart_is_launched_once()
    {
        var restart = new PendingRestart();
        var launched = new List<string>();
        restart.Schedule(@"C:\Apps\Lucarne.exe");

        restart.Start(launched.Add);
        restart.Start(launched.Add);

        Assert.Equal([@"C:\Apps\Lucarne.exe"], launched);
    }

    [Fact]
    public void Only_the_first_schedule_says_it_is_new_and_the_last_path_wins()
    {
        var restart = new PendingRestart();

        Assert.True(restart.Schedule(@"C:\A\Lucarne.exe"));
        Assert.False(restart.Schedule(@"C:\B\Lucarne.exe"));

        var launched = new List<string>();
        restart.Start(launched.Add);
        Assert.Equal([@"C:\B\Lucarne.exe"], launched);
    }

    /// <summary>
    /// 2026-11-11: starting the freshly replaced executable took about fifteen seconds, and the
    /// shutdown watchdog ended the process in the middle of it. The caller that comes second must
    /// wait for the launch in flight, not return at once and let the process die under it.
    /// </summary>
    [Fact]
    public async Task A_second_caller_waits_for_the_launch_in_flight()
    {
        var restart = new PendingRestart();
        restart.Schedule(@"C:\Apps\Lucarne.exe");
        using var inLaunch = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var launches = 0;
        var ct = TestContext.Current.CancellationToken;

        var first = Task.Run(() => restart.Start(_ =>
        {
            Interlocked.Increment(ref launches);
            inLaunch.Set();
            release.Wait(TimeSpan.FromSeconds(10), ct);
        }), ct);
        Assert.True(inLaunch.Wait(TimeSpan.FromSeconds(10), ct));

        var second = Task.Run(() => restart.Start(_ => Interlocked.Increment(ref launches)), ct);
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        Assert.False(second.IsCompleted, "the second caller returned during the launch");

        release.Set();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(1, launches);
    }
}
