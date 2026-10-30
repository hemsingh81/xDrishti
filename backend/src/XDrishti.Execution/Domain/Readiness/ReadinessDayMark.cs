using XDrishti.Domain.Market;

namespace XDrishti.Execution.Domain.Readiness;

/// <summary>What the 09:10:30 mark stores: the state and whether it was written late.</summary>
public sealed record DayMarkDecision(ReadinessState State, bool Late);

/// <summary>
/// The 09:10:30 India-time record of spec P12-11 criterion 4 (D3), kept as a pure rule. On time (within <see cref="OnTime"/> of the mark) it stores the verdict of the probe made at that moment. If the service was not running then, the first run afterwards stores it
/// late and <c>unavailable</c>: a mark is never reconstructed as ready. A check that turns ready after the mark leaves the stored state as it was.
/// </summary>
public static class ReadinessDayMark
{
    public static readonly TimeOnly At = new(9, 10, 30);
    public static readonly TimeSpan OnTime = TimeSpan.FromSeconds(5);

    public static DateTimeOffset MarkTime(DateOnly session) => new(session.ToDateTime(At), IndiaTime.Offset);

    public static bool IsDue(DateTimeOffset now, DateOnly session) => now >= MarkTime(session);

    public static bool IsLate(DateTimeOffset now, DateOnly session) => now - MarkTime(session) > OnTime;

    public static DayMarkDecision Decide(DateTimeOffset now, DateOnly session, ReadinessState probed) =>
        IsLate(now, session) ? new(ReadinessState.Unavailable, true) : new(probed, false);
}
