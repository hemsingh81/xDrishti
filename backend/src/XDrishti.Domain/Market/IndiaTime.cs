namespace XDrishti.Domain.Market;

/// <summary>India Standard Time: UTC+05:30 all year (India has no daylight saving time), so a fixed offset is exact and needs no time-zone database.</summary>
public static class IndiaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    public static DateTimeOffset ToIst(DateTimeOffset instant) => instant.ToOffset(Offset);

    /// <summary>The calendar date in India at the given instant.</summary>
    public static DateOnly Today(DateTimeOffset instant) => DateOnly.FromDateTime(ToIst(instant).DateTime);
}
