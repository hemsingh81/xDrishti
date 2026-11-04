using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Remark;

/// <summary>The rupee arithmetic of the re-mark (spec P14-08, O.2 and D7): units, gross and net. Paise in, rupees out, all in decimal, rounded once to the paisa.</summary>
public static class RemarkMoney
{
    /// <summary>The units of the option that carry the same rupee risk as the cash trade: <c>risk / (|delta| x |entry - stop|)</c>, fractional. Null when the delta is not a usable number or the stop is at the entry.</summary>
    public static decimal? Units(decimal riskRupees, double delta, decimal entry, decimal stop)
    {
        var distance = Math.Abs(entry - stop);
        if (!double.IsFinite(delta) || Math.Abs(delta) < 1e-9 || distance == 0 || riskRupees <= 0)
        {
            return null;
        }

        return riskRupees / ((decimal)Math.Abs(delta) * distance);
    }

    /// <summary>(exit premium - entry premium) x units, in rupees, to the paisa.</summary>
    public static decimal Gross(int entryPremiumPx, int exitPremiumPx, decimal units) =>
        Math.Round((exitPremiumPx - entryPremiumPx) / 100m * units, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Net rupees: gross less the charges of the buy leg and of the sell leg on the owner's sheet, each on its own date, less the half-spread of each leg (P14-07). Null with a fate when the sheet has no option lines for a leg
    /// (<see cref="RemarkFates.BasisMismatch"/>: the option side would not be net while the cash side is) or a spread is unavailable (<see cref="RemarkFates.SpreadUnavailable"/>): a missing figure is never taken as zero.
    /// </summary>
    public static NetOutcome Net(CostSheet sheet, decimal gross, decimal units, int entryPremiumPx, int exitPremiumPx, DateOnly entryDate, DateOnly exitDate, HalfSpreadValue entrySpread, HalfSpreadValue exitSpread)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var buy = OptionLegCost.Evaluate(sheet, CostSegment.Options, Side.Buy, entryPremiumPx / 100m, units, entryDate, entrySpread);
        var sell = OptionLegCost.Evaluate(sheet, CostSegment.Options, Side.Sell, exitPremiumPx / 100m, units, exitDate, exitSpread);
        if (buy.Charges is null || sell.Charges is null)
        {
            return new NetOutcome(null, RemarkFates.BasisMismatch, [buy.Charges is null ? buy.ChargesLabel : sell.ChargesLabel]);
        }

        if (buy.TotalRupees is not { } entryCost || sell.TotalRupees is not { } exitCost)
        {
            return new NetOutcome(null, RemarkFates.SpreadUnavailable, [HalfSpreadValue.Unavailable.Label]);
        }

        var labels = new List<string> { buy.ChargesLabel, entrySpread.Label };
        if (exitSpread.Label != entrySpread.Label)
        {
            labels.Add(exitSpread.Label);
        }

        if (entrySpread.FromRecentArchive || exitSpread.FromRecentArchive)
        {
            labels.Add(HalfSpreadValue.SpreadFromRecentArchive);
        }

        return new NetOutcome(Math.Round(gross - entryCost - exitCost, 2, MidpointRounding.AwayFromZero), null, labels);
    }

    /// <summary>
    /// The units of a defined-risk structure whose maximum loss equals the cash trade's rupee risk (P14-10 decision 3): <c>risk / (max loss per unit in rupees)</c>, fractional. Null when the maximum loss or the risk is not above zero.
    /// </summary>
    public static decimal? UnitsForMaxLoss(decimal riskRupees, int maxLossPx)
    {
        if (riskRupees <= 0 || maxLossPx <= 0)
        {
            return null;
        }

        return riskRupees / (maxLossPx / 100m);
    }

    /// <summary>One fill of a multi-leg structure: the side, the price in paise, the date and the half-spread of that leg at that instant.</summary>
    public sealed record LegFill(Side Side, int PricePx, DateOnly Date, HalfSpreadValue Spread);

    /// <summary>
    /// Net rupees of a structure: gross less the charges of every fill on the owner's sheet and one half-spread per fill (P14-07; four fills for a spread round trip, eight for a condor). As <see cref="Net"/>: no option lines for a fill is
    /// <see cref="RemarkFates.BasisMismatch"/>, a spread that is unavailable is <see cref="RemarkFates.SpreadUnavailable"/>; a missing figure is never taken as zero.
    /// </summary>
    public static NetOutcome NetLegs(CostSheet sheet, decimal gross, decimal units, IReadOnlyList<LegFill> fills)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fills);
        var labels = new List<string>();
        var total = 0m;
        foreach (var fill in fills)
        {
            var cost = OptionLegCost.Evaluate(sheet, CostSegment.Options, fill.Side, fill.PricePx / 100m, units, fill.Date, fill.Spread);
            if (cost.Charges is null)
            {
                return new NetOutcome(null, RemarkFates.BasisMismatch, [cost.ChargesLabel]);
            }

            if (cost.TotalRupees is not { } leg)
            {
                return new NetOutcome(null, RemarkFates.SpreadUnavailable, [HalfSpreadValue.Unavailable.Label]);
            }

            total += leg;
            labels.Add(cost.ChargesLabel);
            labels.Add(fill.Spread.Label);
            if (fill.Spread.FromRecentArchive)
            {
                labels.Add(HalfSpreadValue.SpreadFromRecentArchive);
            }
        }

        return new NetOutcome(Math.Round(gross - total, 2, MidpointRounding.AwayFromZero), null, [.. labels.Distinct(StringComparer.Ordinal)]);
    }
}
