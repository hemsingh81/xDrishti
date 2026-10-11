using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Refresh;

/// <summary>When the daily refresh runs: a fixed time of day in India time (UTC+05:30, no daylight saving).</summary>
public static class RefreshSchedule
{
    /// <summary>The next run strictly after <paramref name="now"/>: today's time if it is still ahead, otherwise tomorrow's.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, TimeOnly timeIst)
    {
        var today = IndiaTime.Today(now);
        var candidate = At(today, timeIst);
        return candidate > now ? candidate : At(today.AddDays(1), timeIst);
    }

    /// <summary>The most recent scheduled instant at or before <paramref name="now"/>: today's time if it has come, otherwise yesterday's.</summary>
    public static DateTimeOffset PreviousRun(DateTimeOffset now, TimeOnly timeIst)
    {
        var today = IndiaTime.Today(now);
        var candidate = At(today, timeIst);
        return candidate <= now ? candidate : At(today.AddDays(-1), timeIst);
    }

    private static DateTimeOffset At(DateOnly date, TimeOnly timeIst) =>
        new(date.ToDateTime(timeIst, DateTimeKind.Unspecified), IndiaTime.Offset);
}
