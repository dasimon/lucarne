namespace Lucarne.Core.Sessions;

/// <summary>
/// How long the next session may take to close when the application is shutting down. Each session
/// gets five seconds, but the whole shutdown is capped: with detached windows the number of live
/// sessions is no longer bounded by what fits in a tab strip.
/// </summary>
public static class ClosePlan
{
    public const int PerSessionSeconds = 5;
    public const int OverallSeconds = 30;

    /// <summary>
    /// How long the UI thread may go without answering, once a shutdown has started, before the
    /// process is ended. Longer than any single wait of a healthy shutdown, which answers between
    /// two sessions; reached only by a thread stuck for good — as inside a Remote Desktop control's
    /// own teardown on 2026-11-10, which no budget can interrupt.
    /// </summary>
    public const int UnresponsiveSeconds = 10;

    /// <summary>True once the UI thread has been silent for <see cref="UnresponsiveSeconds"/>.</summary>
    public static bool IsHung(TimeSpan sinceLastAnswer) => sinceLastAnswer >= TimeSpan.FromSeconds(UnresponsiveSeconds);

    /// <summary>Time granted to the next close: the per-session slice, or whatever budget is left.</summary>
    public static TimeSpan For(int remainingSessions, TimeSpan elapsed)
    {
        if (remainingSessions <= 0) return TimeSpan.Zero;

        var left = TimeSpan.FromSeconds(OverallSeconds) - elapsed;
        if (left <= TimeSpan.Zero) return TimeSpan.Zero;

        var perSession = TimeSpan.FromSeconds(PerSessionSeconds);
        return left < perSession ? left : perSession;
    }
}
