using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// Charges on premium turnover plus the half-spread (spec P14-07). An option leg's turnover is premium times units and a futures leg's is price times units; the charges are those of <see cref="CostModel.Leg"/> for <see cref="CostSegment.Options"/> or
/// <see cref="CostSegment.Futures"/> on the leg's own India date. A sheet without the segment (or no sheet at all) leaves the leg gross, labelled <c>costs: none (no option lines)</c>; it is never charged zero. The half-spread is paid once per leg on the
/// turnover, the buy at the ask and the sell at the bid being a half-spread each way from the mid. Pure; no rate is held here.
/// </summary>
public static class OptionLegCost
{
    public static OptionLegCostResult Evaluate(CostSheet? sheet, CostSegment segment, Side side, decimal price, decimal units, DateOnly date, HalfSpreadValue spread)
    {
        ArgumentNullException.ThrowIfNull(spread);
        if (segment is not (CostSegment.Options or CostSegment.Futures))
        {
            throw new ArgumentOutOfRangeException(nameof(segment), segment, "Only the option and futures segments are costed here.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        var turnover = price * units;

        LegCharges? charges = null;
        string? reason = null;
        var label = CostErrors.NoOptionLinesLabel;
        if (sheet is null)
        {
            reason = CostErrors.CostsUnavailableCode;
        }
        else
        {
            var leg = CostModel.Leg(sheet, segment, side, turnover, date);
            if (leg.IsSuccess)
            {
                charges = leg.Value;
                label = CostDescription.SheetLabel(sheet);
            }
            else
            {
                reason = leg.Error.Code;
                if (reason == CostErrors.NoRatesForDateCode)
                {
                    label = CostErrors.NoRatesForDateLabel;
                }
            }
        }

        decimal? spreadRupees = spread.Available ? Math.Round(turnover * spread.Pct!.Value / 100m, 2, MidpointRounding.AwayFromZero) : null;
        decimal? total = spreadRupees is { } s ? (charges?.Total ?? 0m) + s : null;
        return new OptionLegCostResult(turnover, charges, label, reason, spreadRupees, spread, total);
    }
}
