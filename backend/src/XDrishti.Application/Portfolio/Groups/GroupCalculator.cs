using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio.Groups;

/// <summary>
/// The figures of a group of holdings (ADR 0021): the same pure calculators as the summary — <see cref="ReturnsCalculator"/> (invested, value, profit, XIRR from the union of the members' cash flows) and
/// <see cref="DayChangeCalculator"/> — applied to the group's holdings, so the numbers of a group, of the summary and of a holding agree. Pure: no I/O, no clock (the valuation date is given).
/// </summary>
internal static class GroupCalculator
{
    private const int Decimals = 4;

    public static HoldingGroup Group(GroupSlice slice, DateOnly valuationDate, decimal? scopeValue, IReadOnlyList<GroupPart>? parts)
    {
        ArgumentNullException.ThrowIfNull(slice);
        var f = Figures(slice.Holdings, valuationDate);
        return new HoldingGroup(
            slice.Key, slice.Name, f.Holdings, f.Unpriced, f.Invested, f.Value, f.Pnl, f.PnlPct, f.Realised, f.Xirr, f.XirrState, f.DayChange, f.DayChangePct, f.DayChangeExcluded, Weight(f.Value, scopeValue), parts);
    }

    public static HoldingGroupTotal Total(IReadOnlyList<HoldingData> holdings, DateOnly valuationDate)
    {
        var f = Figures(holdings, valuationDate);
        return new HoldingGroupTotal(
            f.Holdings, f.Unpriced, f.Invested, f.Value, f.Pnl, f.PnlPct, f.Realised, f.Xirr, f.XirrState, f.DayChange, f.DayChangePct, f.DayChangeExcluded, Weight(f.Value, f.Value));
    }

    /// <summary>
    /// The value of the open lots of the priced holdings (quantity x last price, rounded once, as <see cref="ReturnsCalculator"/> does); null when there are holdings and none has a usable price. Only the value is computed — no XIRR and no day change —
    /// so it is cheap enough for the scope value (what the weights are shares of) and for every part of every group.
    /// </summary>
    public static decimal? ValueOf(IReadOnlyList<HoldingData> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        if (holdings.Count > 0 && holdings.All(h => !h.IsPriced))
        {
            return null;
        }

        var value = 0m;
        foreach (var holding in holdings.Where(h => h.IsPriced))
        {
            foreach (var lot in holding.Lots.Where(l => l.IsOpen))
            {
                value += lot.Qty * holding.LastPrice;
            }
        }

        return Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>The value of each part of the group's holdings, largest first (ties by name); parts with nothing valued in them are left out, so the parts sum to the group's value (up to the rounding of each part to four decimals).</summary>
    public static IReadOnlyList<GroupPart> Parts(GroupSlice group, GroupDimension stackBy, Classification classification)
    {
        ArgumentNullException.ThrowIfNull(group);
        return
        [
            .. HoldingPartitions.Partition(group.Holdings, stackBy, classification)
                .Select(part => new GroupPart(part.Key, part.Name, ValueOf(part.Holdings) ?? 0m))
                .Where(part => part.Value > 0m)
                .OrderByDescending(part => part.Value)
                .ThenBy(part => part.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(part => part.Key, StringComparer.Ordinal),
        ];
    }

    private static decimal? Weight(decimal? value, decimal? scopeValue) =>
        value is { } v && scopeValue is > 0m ? Math.Round(v / scopeValue.Value * 100m, Decimals, MidpointRounding.AwayFromZero) : null;

    private static GroupMetrics Figures(IReadOnlyList<HoldingData> holdings, DateOnly valuationDate)
    {
        var returns = ReturnsCalculator.ForGroup(holdings, valuationDate);
        var dayChange = DayChangeCalculator.ForGroup(holdings);
        var unpriced = holdings.Count(h => h.IsOpen && !h.IsPriced);

        // Every holding of the group lacks a usable price: what it is worth is unknown, which is not a value of zero.
        var nothingPriced = holdings.Count > 0 && holdings.All(h => !h.IsPriced);
        var view = HoldingViews.Returns(nothingPriced
            ? returns with { Xirr = new XirrResult(XirrState.NoPrice, null) }
            : returns);
        return new GroupMetrics(
            holdings.Count,
            unpriced,
            view.Invested,
            nothingPriced ? null : view.Value,
            nothingPriced ? null : view.Pnl,
            nothingPriced ? null : view.PnlPct,
            view.RealisedPnl,
            view.Xirr,
            view.XirrState,
            dayChange.Amount,
            dayChange.Pct,
            dayChange.Excluded);
    }

    private sealed record GroupMetrics(
        int Holdings,
        int Unpriced,
        decimal Invested,
        decimal? Value,
        decimal? Pnl,
        decimal? PnlPct,
        decimal Realised,
        decimal? Xirr,
        string XirrState,
        decimal? DayChange,
        decimal? DayChangePct,
        int DayChangeExcluded);
}
