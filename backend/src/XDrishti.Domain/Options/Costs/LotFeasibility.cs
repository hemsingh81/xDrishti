namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// The lot-feasibility count (spec P14-07, P14-00 D7): research sizes in fractional units; whether one lot fits is counted apart. <c>loss_per_lot = lot x |delta| x |entry - stop|</c> against the cash trade's rupee risk
/// <c>qty x |entry - stop|</c>: above it is <c>not_sizable</c>. A lot that <see cref="LotSizeLookup"/> cannot give is <c>lot_unknown</c> (the risk is still computed); a delta that is not a number is <c>delta_unavailable</c>; a stop at the entry or a quantity
/// not above zero is <c>risk_invalid</c>. Pure.
/// </summary>
public static class LotFeasibility
{
    public const string UnconfirmedLabel = "lot: unconfirmed";

    public static LotFeasibilityResult Evaluate(LotInForce lot, double? delta, decimal entry, decimal stop, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(lot);
        var distance = Math.Abs(entry - stop);
        var risk = Math.Max(quantity, 0m) * distance;
        if (!lot.Available)
        {
            return new LotFeasibilityResult(FeasibilityVerdict.LotUnknown, "lot_unknown", risk, null, null, null);
        }

        var label = lot.Confirmed ? null : UnconfirmedLabel;
        if (quantity <= 0 || distance == 0)
        {
            return new LotFeasibilityResult(FeasibilityVerdict.RiskInvalid, "risk_invalid", risk, null, lot.LotSize, label);
        }

        if (delta is not { } d || !double.IsFinite(d) || Math.Abs(d) > 1)
        {
            return new LotFeasibilityResult(FeasibilityVerdict.DeltaUnavailable, "delta_unavailable", risk, null, lot.LotSize, label);
        }

        var loss = lot.LotSize * (decimal)Math.Abs(d) * distance;
        return loss > risk
            ? new LotFeasibilityResult(FeasibilityVerdict.NotSizable, "not_sizable", risk, loss, lot.LotSize, label)
            : new LotFeasibilityResult(FeasibilityVerdict.Sizable, "sizable", risk, loss, lot.LotSize, label);
    }
}
