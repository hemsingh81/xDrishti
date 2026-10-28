using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// Whether an entry had a broker-side stop (spec P11-01 criterion 7). Protected: an order of the other side, with a trigger price, for at least the position, in a state that is or was in force, created no later than the last entry fill plus the
/// window (default 300 s). Otherwise an intraday product is <c>unprotected</c> and a delivery product <c>protection_unknown</c> (forever and super orders are not read). With no order evidence the answer is <c>unavailable</c>, never a pass. Pure.
/// </summary>
public static class ProtectionCheck
{
    /// <summary>Products that net out within the day: a missing stop is a finding for them.</summary>
    private static readonly string[] Intraday = ["INTRADAY", "INTRA", "MIS", "CO", "BO"];

    public static string Evaluate(DateTimeOffset lastEntryFill, Side entrySide, string product, long positionQty, IReadOnlyList<LiveOrder>? orders, TimeSpan within)
    {
        if (orders is null)
        {
            return OutcomeCodes.Unavailable;
        }

        var deadline = lastEntryFill + within;
        var covered = orders.Any(o => o.Side != entrySide && o.TriggerPrice is > 0m && o.Qty >= positionQty && LiveOrderStatus.CountsAsStop(o.Status) && o.CreateTime <= deadline);
        if (covered)
        {
            return OutcomeCodes.Protected;
        }

        return Intraday.Contains(product.Trim().ToUpperInvariant(), StringComparer.Ordinal) ? OutcomeCodes.Unprotected : OutcomeCodes.ProtectionUnknown;
    }
}
