using XDrishti.Domain.Market;

namespace XDrishti.Execution.Domain.Readiness;

/// <summary>When the loop reads and when it stores (spec P12-11 criterion 1). Hours are India time; nothing is read outside 08:00 to 16:30 and nothing on a closed day.</summary>
public static class ReadinessSchedule
{
    public static readonly TimeOnly WindowStart = new(8, 0);
    public static readonly TimeOnly WindowEnd = new(16, 30);
    public static readonly TimeOnly FastStart = new(9, 0);
    public static readonly TimeOnly FastEnd = new(9, 15);
    public static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(15);

    public static bool InWindow(DateTimeOffset now)
    {
        var t = TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime);
        return t >= WindowStart && t <= WindowEnd;
    }

    /// <summary>How long to wait after a probe: 30 seconds from 09:00 to 09:15 while the account is not ready, else the configured interval.</summary>
    public static TimeSpan Interval(DateTimeOffset now, bool ready, int intervalSeconds)
    {
        var t = TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime);
        return !ready && t >= FastStart && t < FastEnd ? FastInterval : TimeSpan.FromSeconds(intervalSeconds);
    }

    /// <summary>A row is stored when nothing is stored yet, the verdict or a reason changed, or the last row is at least 15 minutes old.</summary>
    public static bool ShouldStore(bool changed, DateTimeOffset? lastAt, DateTimeOffset now) => changed || lastAt is null || now - lastAt >= MaxGap;
}
