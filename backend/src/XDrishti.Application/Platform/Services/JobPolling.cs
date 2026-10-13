namespace XDrishti.Application.Platform.Services;

/// <summary>
/// How a service that hands its work to a background job looks at the job while it waits for it: first soon (a short job ends the run at once), then less and less often, never rarer than every
/// <see cref="MaxWait"/>. Every wait is read from the <see cref="TimeProvider"/>, so tests need no sleeping.
/// </summary>
public static class JobPolling
{
    public static readonly TimeSpan FirstWait = TimeSpan.FromMilliseconds(100);

    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    /// <summary>The wait after <paramref name="current"/>: twice as long, at most <see cref="MaxWait"/>.</summary>
    public static TimeSpan Next(TimeSpan current) => TimeSpan.FromTicks(Math.Min(current.Ticks * 2, MaxWait.Ticks));
}
