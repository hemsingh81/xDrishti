namespace XDrishti.Domain.Options.Pricing;

/// <summary>Time to expiry as the ADR 0178 convention: the expiry date at 15:30 India time (a fixed +05:30 offset, no daylight saving) minus <c>at</c>, in seconds, divided by 365 x 86,400. At or after the expiry instant it is <c>expired</c>.</summary>
public static class ExpiryTime
{
    private const double SecondsPerYear = 365.0 * 86_400.0;
    private static readonly TimeSpan India = new(5, 30, 0);

    public static PricedValue<double> YearFraction(DateTimeOffset at, DateOnly expiry)
    {
        var end = new DateTimeOffset(expiry.Year, expiry.Month, expiry.Day, 15, 30, 0, India);
        var seconds = (end - at).TotalSeconds;
        return seconds > 0 ? Priced.Of(seconds / SecondsPerYear) : Priced.Unavailable<double>(UnavailableReason.Expired);
    }
}
