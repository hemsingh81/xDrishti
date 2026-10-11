namespace XDrishti.Application.Accounts;

/// <summary>Dates the owner means are calendar dates in India (the market's time zone).</summary>
internal static class AccountTime
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

    public static DateOnly TodayInIndia(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(IstOffset).DateTime);
}
