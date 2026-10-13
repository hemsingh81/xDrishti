namespace XDrishti.Application.Portfolio.Trades;

// Request and response shapes of the trade import and lot reconstruction API (ADR 0023, P2-03). Dates are India dates, times India times, amounts rupees.

/// <summary>Rows of the file that were left out because they are not NSE equity trades: how many, and how many for each reason (<c>derivatives</c>, <c>commodity</c>, <c>currency</c>, <c>bse</c>, <c>non_equity</c>).</summary>
public sealed record TradeSkipped(int Total, IReadOnlyDictionary<string, int> ByReason);

/// <summary>
/// What was in the file. <c>Rows</c> are the data rows (empty lines do not count), <c>Parsed</c> the valid trade rows, <c>Duplicates</c> the valid rows that repeat a trade id of the file, <c>AlreadyImported</c> the trades of the file that are stored already
/// (and not voided), <c>NewTrades</c> the ones an import would store. <c>Bad</c> lists the first 200 bad rows (line, reason); <c>BadTotal</c> counts all of them.
/// </summary>
public sealed record TradeFileSummary(
    string FileName,
    int Rows,
    int Parsed,
    TradeSkipped Skipped,
    IReadOnlyList<TradeBadRow> Bad,
    int BadTotal,
    int Duplicates,
    int AlreadyImported,
    int NewTrades);

/// <summary>A number of securities and the number of trades of the file in them.</summary>
public sealed record TradeCount(int Securities, int Trades);

/// <summary>The quantity the lots count as opening (the shares the trades do not explain): now, and after the rebuild. Null where it cannot be told (no holding, or a quantity that is not whole after a split or bonus).</summary>
public sealed record OpeningQtyView(long? Before, long? After);

/// <summary>
/// What rebuilding a holding's lots from its trades does. <c>Status</c>: <c>ok</c> (the lots would change), <c>unchanged</c>, <c>quantity_mismatch</c> (see <c>Reason</c>: <c>negative_opening</c>, <c>sell_exceeds_open</c>,
/// <c>fractional_after_adjustment</c>; nothing is applied), <c>no_holding</c>, <c>ambiguous</c>. <c>Q</c> is the broker's quantity, <c>Net</c> bought minus sold (today's shares). <c>HandDatedLotsReplaced</c> counts the lots whose date the owner chose
/// and <c>SyncLotsReplaced</c> the lots a sync added, among the <c>LotsBefore</c> lots that an <c>ok</c> rebuild replaces by <c>LotsAfter</c> lots. <c>RealisedPnl</c> is the profit of the pieces the trades sold. <c>AdjustedFor</c> counts the corporate
/// actions that changed a trade. <c>Digest</c> fingerprints this rebuild. <c>OpenCostAfter</c> is what the open lots cost after the rebuild (Σ open quantity × unit cost; also for <c>unchanged</c>), <c>BrokerCost</c> the broker's <c>Q × A</c>
/// and <c>CostDelta</c> their difference. The open quantities always add up to <c>Q</c>. With no sales in the file and an opening lot (the file does not explain the whole holding) <c>CostDelta</c> is 0, to within the rounding of the opening
/// lot's unit cost to 4 decimals (0.0001 × its quantity); with sales — or without an opening lot — the first-in first-out cost basis of the trades can differ from the broker's average-cost figure (brokers keep the average by the
/// weighted-average-cost method, a sale does not change it), which is shown, not hidden. <c>CostNote</c> is <c>opening_cost_fallback</c> when the trades cost more than the broker's average cost allows and the opening lot therefore costs the
/// average cost. The four are null unless the trades fit (<c>ok</c> or <c>unchanged</c>).
/// </summary>
public sealed record TradeRebuildView(
    string Status,
    string? Reason,
    int LotsBefore,
    int LotsAfter,
    int HandDatedLotsReplaced,
    int SyncLotsReplaced,
    decimal? RealisedPnl,
    int AdjustedFor,
    long? Q,
    long? Net,
    OpeningQtyView OpeningQty,
    string Digest,
    decimal? OpenCostAfter = null,
    decimal? BrokerCost = null,
    decimal? CostDelta = null,
    string? CostNote = null);

/// <summary>A security of the file: its trades in the file and, when the account holds it, what the rebuild would do. <c>Matched</c> is true when it is a holding of the account or an NSE share or ETF of the master list.</summary>
public sealed record TradeSecurityView(
    string Symbol,
    string? Isin,
    bool Matched,
    Guid? HoldingId,
    int Trades,
    int Buys,
    int Sells,
    DateOnly FirstTradeDate,
    DateOnly LastTradeDate,
    TradeRebuildView Rebuild);

/// <summary>
/// The preview of a tradebook import — nothing is stored. <c>NotHeld</c>: securities the account does not hold now (their trades are stored by an import, not applied); <c>Unmatched</c>: those of them that are not even NSE shares or ETFs of the master list.
/// <c>Digest</c> fingerprints everything the preview shows (all securities, listed or not); an import with <c>rebuild</c> must send it back. <c>Securities</c> lists every security that is a holding or an NSE share/ETF of the
/// master list and at most <see cref="TradeCsvLimits.MaxListedUnmatched"/> (200) of the unmatched ones; <c>SecuritiesTotal</c> counts all the securities of the file and <c>UnmatchedListed</c> the unmatched ones listed.
/// </summary>
public sealed record TradePreviewReport(
    TradeFileSummary File,
    IReadOnlyList<TradeSecurityView> Securities,
    TradeCount NotHeld,
    TradeCount Unmatched,
    string Digest,
    int SecuritiesTotal = 0,
    int UnmatchedListed = 0);

/// <summary>One import of a file. <c>Status</c> is <c>imported</c> or <c>undone</c>; <c>ActiveTrades</c> counts its trades that are not voided and <c>Securities</c> the securities among them.</summary>
public sealed record TradeBatchView(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string FileName,
    string Sha256,
    int Parsed,
    int Imported,
    int Duplicates,
    int Skipped,
    int Bad,
    string Status,
    int ActiveTrades,
    int Securities,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UndoneAt);

/// <summary>One page of import batches, newest first.</summary>
public sealed record TradeBatchPage(IReadOnlyList<TradeBatchView> Items, int Page, int PageSize, int Total);

/// <summary>
/// What an import did. <c>Batch</c> is null when the file had nothing new. <c>Imported</c> trades were stored, <c>AlreadyImported</c> were there already. With <c>Rebuilt</c> the lots of <c>HoldingsRebuilt</c> holdings were replaced
/// (<c>LotsReplaced</c> lots removed); <c>Report</c> is the preview the owner confirmed (statuses before the rebuild).
/// </summary>
public sealed record TradeImportResult(TradeBatchView? Batch, int Imported, int AlreadyImported, bool Rebuilt, int HoldingsRebuilt, int LotsReplaced, TradePreviewReport Report);

/// <summary>A holding (or security) that an undo could not rebuild: its <c>Symbol</c>, the <c>HoldingId</c> when it is a single holding, and the <c>Reason</c> — <c>ambiguous</c> or the mismatch (<c>negative_opening</c>, <c>sell_exceeds_open</c>, <c>fractional_after_adjustment</c>).</summary>
public sealed record TradeNotRebuilt(string Symbol, Guid? HoldingId, string Reason);

/// <summary>
/// What undoing a batch did — or, with <c>DryRun</c>, would do (nothing is changed then; <c>Applied</c> is false). Undoing a batch again is a no-op (<c>AlreadyUndone</c>). Holdings whose lots came from trades are rebuilt from the remaining
/// trades; <c>NotRebuilt</c> lists those that could not be (a mismatch, or ambiguous) and keep their lots, <c>HoldingsNotRebuilt</c> counts them. <b>An undo cannot bring back</b> lots the owner dated by hand or a sync added that an
/// earlier rebuild replaced: the holding returns to one default-dated lot of the broker's quantity and cost (or to the shape of the remaining trades); <c>HandDatedLotsReplaced</c> and <c>SyncLotsReplaced</c> count what the rebuild
/// removes (the lots of the holdings it rebuilds).
/// </summary>
public sealed record TradeUndoResult(
    TradeBatchView Batch,
    bool AlreadyUndone,
    int Voided,
    int HoldingsRebuilt,
    int LotsReplaced,
    int HoldingsNotRebuilt,
    bool DryRun,
    bool Applied,
    int HandDatedLotsReplaced,
    int SyncLotsReplaced,
    IReadOnlyList<TradeNotRebuilt> NotRebuilt);

/// <summary>A trade. <c>Side</c> is <c>buy</c> or <c>sell</c>, <c>Source</c> is <c>csv</c> or <c>manual</c>. A voided trade does not count.</summary>
public sealed record TradeView(
    Guid Id,
    Guid AccountId,
    string Symbol,
    string? Isin,
    string Exchange,
    string Side,
    DateOnly TradeDate,
    TimeOnly? TradeTime,
    long Qty,
    decimal Price,
    decimal Charges,
    string Source,
    Guid? BatchId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    bool Voided,
    DateTimeOffset? VoidedAt,
    string? VoidedBy);

/// <summary>One page of the trades of a holding, newest first, voided ones included.</summary>
public sealed record TradePage(IReadOnlyList<TradeView> Items, int Page, int PageSize, int Total);

/// <summary>
/// A trade typed in by the owner: for a holding (<c>HoldingId</c>) or for a security of an account (<c>AccountId</c> and <c>Symbol</c> and/or <c>Isin</c>, which must be an NSE share or ETF of the master list). <c>Side</c> buy or sell, the date not in the
/// future, <c>Quantity</c> a whole number above 0, <c>Price</c> above 0, <c>Charges</c> optional (0).
/// </summary>
public sealed record ManualTradeInput(
    Guid? AccountId,
    Guid? HoldingId,
    string? Symbol,
    string? Isin,
    string? Side,
    DateOnly? TradeDate,
    TimeOnly? TradeTime,
    decimal? Quantity,
    decimal? Price,
    decimal? Charges);

/// <summary>The rebuild of one holding's lots: with <c>DryRun</c> only what it would do; otherwise <c>Applied</c> says whether the lots were replaced (only an <c>ok</c> rebuild is).</summary>
public sealed record HoldingRebuildResult(bool DryRun, bool Applied, TradeRebuildView Rebuild);
