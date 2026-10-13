using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Turns the plans and the stored trades into the shapes of the API.</summary>
internal static class TradeViews
{
    public static TradeRebuildView Rebuild(HoldingRebuild rebuild) => new(
        rebuild.Status,
        rebuild.Reason,
        rebuild.LotsBefore,
        rebuild.LotsAfter,
        rebuild.HandDatedLotsReplaced,
        rebuild.SyncLotsReplaced,
        rebuild.RealisedPnl,
        rebuild.AdjustedFor,
        rebuild.Qty,
        rebuild.Net,
        new OpeningQtyView(rebuild.OpeningBefore, rebuild.OpeningAfter),
        rebuild.Digest,
        rebuild.OpenCostAfter,
        rebuild.BrokerCost,
        rebuild.CostDelta,
        rebuild.CostNote);

    public static TradeSecurityView Security(SecurityPlan security) => new(
        security.Symbol,
        security.Isin,
        security.Matched,
        security.HoldingId,
        security.Trades,
        security.Buys,
        security.Sells,
        security.FirstTradeDate,
        security.LastTradeDate,
        Rebuild(security.Rebuild));

    public static TradePreviewReport Report(string fileName, TradeFile file, TradePlan plan, int alreadyImported, int newTrades)
    {
        // Every security the owner can act on is listed; a tradebook can have hundreds of symbols that are neither held nor listed, which are counted and only the first ones listed.
        var unmatched = plan.Securities.Where(s => !s.Matched).Take(TradeCsvLimits.MaxListedUnmatched).ToList();
        var listed = plan.Securities.Where(s => s.Matched).Concat(unmatched).OrderBy(s => s.Symbol, StringComparer.Ordinal).ThenBy(s => s.Key, StringComparer.Ordinal).Select(Security).ToList();
        return Build(fileName, file, plan, alreadyImported, newTrades, listed, unmatched.Count);
    }

    private static TradePreviewReport Build(string fileName, TradeFile file, TradePlan plan, int alreadyImported, int newTrades, List<TradeSecurityView> listed, int unmatchedListed) => new(
        new TradeFileSummary(
            fileName,
            file.Rows,
            file.Parsed,
            new TradeSkipped(file.Skipped, file.SkippedByReason),
            file.Bad,
            file.BadTotal,
            file.DuplicatesInFile,
            alreadyImported,
            newTrades),
        listed,
        new TradeCount(plan.NotHeldSecurities, plan.NotHeldTrades),
        new TradeCount(plan.UnmatchedSecurities, plan.UnmatchedTrades),
        plan.Digest,
        plan.Securities.Count,
        unmatchedListed);

    public static TradeBatchView Batch(TradeBatchRow row, string accountName) => new(
        row.Batch.Id,
        row.Batch.AccountId,
        accountName,
        row.Batch.FileName,
        row.Batch.Sha256,
        row.Batch.Parsed,
        row.Batch.Imported,
        row.Batch.Duplicates,
        row.Batch.Skipped,
        row.Batch.Bad,
        row.Batch.Status.ToString().ToLowerInvariant(),
        row.ActiveTrades,
        row.Securities,
        row.Batch.CreatedBy,
        row.Batch.CreatedAt,
        row.Batch.UndoneAt);

    public static TradeView Trade(Transaction trade) => new(
        trade.Id,
        trade.AccountId,
        trade.TradingSymbol,
        trade.Isin,
        trade.Exchange,
        trade.Side.ToString().ToLowerInvariant(),
        trade.TradeDate,
        trade.TradeTime,
        trade.Qty,
        trade.Price,
        trade.Charges,
        trade.Source.ToString().ToLowerInvariant(),
        trade.BatchId,
        trade.CreatedBy,
        trade.CreatedAt,
        trade.IsVoided,
        trade.VoidedAt,
        trade.VoidedBy);
}
