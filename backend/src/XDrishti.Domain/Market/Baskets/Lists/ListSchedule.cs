namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>When a list file is due: at the latest scheduled India time of day that has passed (today's or yesterday's) the source has not been checked since. Pure.</summary>
public static class ListSchedule
{
    /// <summary>The latest scheduled instant at or before <paramref name="now"/>; null when there are no times.</summary>
    public static DateTimeOffset? LatestScheduled(DateTimeOffset now, IReadOnlyCollection<TimeOnly> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        DateTimeOffset? latest = null;
        var today = IndiaTime.Today(now);
        foreach (var day in new[] { today.AddDays(-1), today })
        {
            foreach (var time in times)
            {
                var at = new DateTimeOffset(day.ToDateTime(time), IndiaTime.Offset);
                if (at <= now && (latest is null || at > latest))
                {
                    latest = at;
                }
            }
        }

        return latest;
    }

    public static bool IsDue(DateTimeOffset now, DateTimeOffset? lastChecked, IReadOnlyCollection<TimeOnly> times) =>
        LatestScheduled(now, times) is { } scheduled && (lastChecked is null || lastChecked < scheduled);
}
