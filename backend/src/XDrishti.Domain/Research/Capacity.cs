namespace XDrishti.Domain.Research;

/// <summary>
/// The capacity rule of CF-05 (D4, UNVERIFIED rule of thumb): an order is at most 1 % of the fill minute's traded value and at most 0.5 % of the 20-day median daily value. The shares are arguments (settings), not constants. A breach marks the trade
/// <c>capacity_breach</c>. Pure.
/// </summary>
public static class Capacity
{
    /// <summary>Proposed share of the fill minute's traded value.</summary>
    public const decimal ProposedMinuteShare = 0.01m;

    /// <summary>Proposed share of the 20-day median daily traded value.</summary>
    public const decimal ProposedDailyShare = 0.005m;

    /// <param name="orderValue">Quantity times the filled entry price in rupees.</param>
    /// <param name="minuteTradedValue">The fill minute's traded value in rupees; null when unknown.</param>
    /// <param name="dailyMedianValue">The 20-day median daily traded value in rupees; null when unknown.</param>
    /// <param name="minuteShare">The share of the minute allowed.</param>
    /// <param name="dailyShare">The share of the daily median allowed.</param>
    public static CapacityCheck Check(decimal orderValue, decimal? minuteTradedValue, decimal? dailyMedianValue, decimal minuteShare, decimal dailyShare)
    {
        decimal? minuteCap = minuteTradedValue is { } m && m >= 0 ? m * minuteShare : null;
        decimal? dailyCap = dailyMedianValue is { } d && d >= 0 ? d * dailyShare : null;
        if (minuteCap is null || dailyCap is null)
        {
            return new CapacityCheck(true, minuteCap, dailyCap, "unavailable: " + (minuteCap is null ? "the fill minute's traded value" : "the 20-day median traded value") + " is not known");
        }

        var reasons = new List<string>(2);
        if (orderValue > minuteCap)
        {
            reasons.Add("over the share of the fill minute's traded value");
        }

        if (orderValue > dailyCap)
        {
            reasons.Add("over the share of the 20-day median traded value");
        }

        return new CapacityCheck(reasons.Count > 0, minuteCap, dailyCap, string.Join("; ", reasons));
    }
}
