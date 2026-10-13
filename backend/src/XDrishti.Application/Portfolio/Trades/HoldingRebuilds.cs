using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Works out the rebuild of one holding from its trades (ADR 0023): runs the reconstruction and compares the result with the lots there are. Pure.</summary>
internal static class HoldingRebuilds
{
    public static HoldingRebuild Compute(Holding holding, IReadOnlyList<Transaction> trades, IReadOnlyList<AppliedAction> actions, DateOnly firstImportDate)
    {
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(trades);
        var rebuilt = LotReconstructor.Reconstruct(new ReconInput(holding.Qty, holding.AvgCost, firstImportDate, [.. trades.Select(ReconTrade.Of)], actions));
        var current = LotShapes.Of(holding.Lots);
        var openingBefore = holding.Lots.Where(l => l.Source == LotSource.Import && (l.IsOpen || !l.Estimated)).Sum(l => l.Qty);
        var before = holding.Lots.Count;
        var key = holding.Id.ToString("N");
        if (!rebuilt.Applicable)
        {
            var failed = LotShapes.Digest([key, RebuildStatuses.QuantityMismatch, rebuilt.Reason ?? string.Empty, current, holding.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            return new HoldingRebuild(
                holding, RebuildStatuses.QuantityMismatch, rebuilt.Reason, before, before, 0, 0, null, rebuilt.AdjustedFor, holding.Qty, rebuilt.Net, openingBefore, rebuilt.OpeningQty, [], failed);
        }

        var openCost = rebuilt.Lots.Where(l => l.IsOpen).Sum(l => l.Qty * l.UnitCost);
        var brokerCost = holding.Qty * holding.AvgCost;
        var delta = openCost - brokerCost;
        var unchanged = current == LotShapes.Of(rebuilt.Lots);
        var status = unchanged ? RebuildStatuses.Unchanged : RebuildStatuses.Ok;
        var handDated = unchanged ? 0 : holding.Lots.Count(l => l.Source == LotSource.Manual || (l.Source == LotSource.Import && !l.UsesDefaultDate));
        var sync = unchanged ? 0 : holding.Lots.Count(l => l.Source == LotSource.Added);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var digest = LotShapes.Digest(
        [
            key, status, current, LotShapes.Of(rebuilt.Lots), holding.Qty.ToString(invariant), rebuilt.AdjustedFor.ToString(invariant),
            openCost.ToString("0.0000", invariant), brokerCost.ToString("0.0000", invariant), delta.ToString("0.0000", invariant), rebuilt.CostNote ?? string.Empty,
        ]);
        return new HoldingRebuild(
            holding, status, null, before, unchanged ? before : rebuilt.Lots.Count, handDated, sync, rebuilt.RealisedPnl, rebuilt.AdjustedFor, holding.Qty, rebuilt.Net, openingBefore, rebuilt.OpeningQty, unchanged ? [] : rebuilt.Lots, digest, openCost, brokerCost, delta, rebuilt.CostNote);
    }

    /// <summary>The trades among <paramref name="candidates"/> (a security's trades, read by ISIN or symbol) that the matcher resolves to this holding and no other.</summary>
    public static List<Transaction> Of(HoldingMatcher matcher, Holding holding, IEnumerable<Transaction> candidates)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(candidates);
        return [.. candidates.Where(t => matcher.Find(t.Isin, t.TradingSymbol) is { Kind: MatchKind.One, Holding: { } found } && found.Id == holding.Id)];
    }

    /// <summary>The trades that belong to each holding (by <see cref="HoldingMatcher"/>); trades of securities with no single holding are left out.</summary>
    public static Dictionary<Guid, List<Transaction>> Group(HoldingMatcher matcher, IEnumerable<Transaction> trades)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(trades);
        var grouped = new Dictionary<Guid, List<Transaction>>();
        foreach (var trade in trades)
        {
            if (matcher.Find(trade.Isin, trade.TradingSymbol) is { Kind: MatchKind.One, Holding: { } holding })
            {
                if (!grouped.TryGetValue(holding.Id, out var list))
                {
                    grouped[holding.Id] = list = [];
                }

                list.Add(trade);
            }
        }

        return grouped;
    }
}
