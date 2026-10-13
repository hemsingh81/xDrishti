namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// The unit-value (NAV) series of ADR 0020 §4, which makes cash flows neutral so a deposit never looks like a gain nor a withdrawal like a loss. The first NAV is 100. Each day, with <c>units</c> issued so far:
/// <c>NAV = value of what was held the day before, at today's prices ÷ units</c> (the previous NAV when no units exist); then the day's net flow, valued at today's prices and so not changing the NAV, issues units
/// (<c>flow ÷ NAV</c>, negative for a sale) — and every unit is cancelled when nothing is held any more, the NAV carrying on from where it was. Pure.
/// </summary>
public static class NavSeries
{
    public const decimal Start = 100m;

    /// <summary>
    /// The NAV of each day. <paramref name="carriedValue"/>[d] is the value, at day d's prices, of what was held after day d-1 (0 on the first day); <paramref name="flow"/>[d] the net amount put in on day d (purchases
    /// minus sales, at day d's prices); <paramref name="empty"/>[d] whether nothing is held after day d.
    /// </summary>
    public static decimal[] Compute(IReadOnlyList<decimal> carriedValue, IReadOnlyList<decimal> flow, IReadOnlyList<bool> empty)
    {
        ArgumentNullException.ThrowIfNull(carriedValue);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(empty);
        var nav = new decimal[carriedValue.Count];
        var units = 0m;
        var current = Start;
        for (var d = 0; d < nav.Length; d++)
        {
            if (units > 0m && carriedValue[d] > 0m)
            {
                current = carriedValue[d] / units;
            }

            nav[d] = current;
            units = empty[d] ? 0m : Math.Max(0m, units + (flow[d] / current));
        }

        return nav;
    }
}
