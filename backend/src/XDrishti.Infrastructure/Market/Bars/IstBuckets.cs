namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>Where the buckets of a changed range begin and end, in India time (a fixed UTC+05:30 offset): what the aggregate statements read, as UTC instants.</summary>
internal static class IstBuckets
{
    private static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    /// <summary>India midnight of the day that contains the instant.</summary>
    public static DateTimeOffset DayStart(DateTimeOffset instant)
    {
        var local = instant.ToUniversalTime() + Offset;
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Offset).ToUniversalTime();
    }

    /// <summary>India midnight of the Monday of the week that contains the instant.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset instant)
    {
        var day = DayStart(instant);
        var local = day + Offset;
        var back = ((int)local.DayOfWeek + 6) % 7;
        return day.AddDays(-back);
    }

    /// <summary>India midnight of the first day of the month that contains the instant.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset instant)
    {
        var local = instant.ToUniversalTime() + Offset;
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, Offset).ToUniversalTime();
    }

    /// <summary>India midnight of the first day of the month after the one that contains the instant.</summary>
    public static DateTimeOffset NextMonthStart(DateTimeOffset instant)
    {
        var local = instant.ToUniversalTime() + Offset;
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, Offset).AddMonths(1).ToUniversalTime();
    }
}
