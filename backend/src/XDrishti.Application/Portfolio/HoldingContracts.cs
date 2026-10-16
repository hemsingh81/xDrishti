using XDrishti.Application.Portfolio.Income;

namespace XDrishti.Application.Portfolio;

// Request and response shapes of the holdings API. Percentages (PnlPct, Xirr) are percent numbers: 16.1 means 16.1 %. Money is in rupees.
// XirrState: "ok", "insufficient" (shown as "—"), "n_m" (beyond ±1,000 %, shown as "n/m"), "no_data", or "no_price" (the holding has no usable price,
// Priced = false: Value, Pnl, PnlPct and Xirr are null — its worth is unknown, it is not a total loss — and it is left out of group and summary figures). Dates are India calendar dates.

/// <summary>The profit/loss figures that every holding row, lot row and summary carries.</summary>
public sealed record ReturnsView(
    decimal Invested,
    decimal? Value,
    decimal? Pnl,
    decimal? PnlPct,
    decimal? Xirr,
    string XirrState,
    decimal RealisedPnl,
    bool RealisedEstimated);

/// <summary>
/// One holding. Status is "open" or "closed". Mapped is false when the holding is not in the instrument master (InstrumentId and CanonicalSymbol are
/// then null). LotCount counts open lots; EarliestAcquiredOn is the oldest open lot's date; UsesDefaultDate is true when any open lot has the
/// import's default date. Returns cover all lots (closed lots add to RealisedPnl and to the XIRR).
/// PreviousClose is the previous session's close as the last quote reported it (null until a quote gave one). DayChange (rupees) and DayChangePct (percent number) are
/// against it, for an open, priced holding with a previous close; otherwise both are null and the holding is left out of the summary's day change. PriceAt is the time of
/// LastPrice (the same instant as LastPriceAt) and PriceSource says where the price came from: "sync" (the holdings list at the last sync), "quote" (a market quote) or "live" (a tick of the live feed).
/// Income is the holding's dividends and trade charges and the net returns that include them (ADR 0026); null when nothing is recorded for the holding. It does not change with the price, except the two total return percentages: the
/// browser works those out again at a live price from Returns.RealisedPnl, Returns.Pnl, Returns.Invested and Income's Dividends and Charges.
/// </summary>
public sealed record HoldingRow(
    Guid Id,
    Guid AccountId,
    string AccountName,
    Guid BucketId,
    string BucketName,
    Guid? InstrumentId,
    string? CanonicalSymbol,
    bool Mapped,
    string Exchange,
    string SecurityId,
    string? Isin,
    string Symbol,
    string Name,
    long Qty,
    decimal AvgCost,
    decimal LastPrice,
    DateTimeOffset? LastPriceAt,
    string Status,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset SyncedAt,
    int LotCount,
    DateOnly? EarliestAcquiredOn,
    bool UsesDefaultDate,
    bool Priced,
    ReturnsView Returns,
    decimal? PreviousClose,
    decimal? DayChange,
    decimal? DayChangePct,
    DateTimeOffset? PriceAt,
    string PriceSource,
    IncomeView? Income = null);

/// <summary>One page of holdings (Total counts all matches).</summary>
public sealed record HoldingPage(IReadOnlyList<HoldingRow> Items, int Page, int PageSize, int Total);

/// <summary>
/// Body of "Sync now" (optional). Confirm: true applies an answer that would close all or most open holdings, which is otherwise refused with
/// <c>409 portfolio.confirmation_required</c> (the problem carries <c>listedHoldings</c>, <c>openHoldings</c> and <c>wouldClose</c>). With Confirm: true both
/// ExpectedOpenHoldings and ExpectedWouldClose are required (400 portfolio.invalid otherwise): the numbers the owner saw in the 409. If Dhan's answer differs
/// now, nothing is changed and the sync answers <c>409 portfolio.confirmation_stale</c> with the fresh numbers.
/// </summary>
public sealed record SyncInput(bool? Confirm, int? ExpectedOpenHoldings = null, int? ExpectedWouldClose = null);

/// <summary>A run of the holdings sync. Trigger "manual", "auto" or "import"; Status "running", "succeeded", "failed" or "skipped". Error is a short safe text.</summary>
public sealed record SyncRunView(
    Guid Id,
    Guid AccountId,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int NewCount,
    int IncreasedCount,
    int DecreasedCount,
    int ClosedCount,
    int UnchangedCount,
    string? Error);

/// <summary>
/// Totals over the holdings of the filter (all accounts that count in totals, one account, one bucket). DefaultDateLots counts open lots that
/// still have the import's default date. UnpricedCount counts open holdings without a usable price: they are left out of Returns (invested, value, P&amp;L, XIRR). ValuationDate is the day the values are as of (today in India).
/// DayChange (rupees) is the sum of the day changes of the included open holdings and DayChangePct (percent number) is that sum over the sum of qty x previous close of the same holdings; both are null when no
/// holding is included. DayChangeExcluded counts the open holdings left out (no previous close or no usable price). PricesAsOf holds the oldest and the newest price time of the open priced holdings.
/// BenchmarkXirr (percent number) is the XIRR of the same cash flows put into the benchmark index (Nifty 50 by default, ADR 0020 §2), Alpha the portfolio's XIRR minus it in percentage points; BenchmarkState is "ok", "unavailable" (the index has no history
/// from the first cash flow: track it and run Update data), "insufficient" (the same reasons as for XIRR) or "n_m" (beyond ±1,000 %). BenchmarkXirr and Alpha are null unless their rates are meaningful (Alpha also needs the portfolio's XirrState "ok").
/// BenchmarkAsOf is the date of the index close the benchmark was valued at: the index has no live quote, so on a trading day before its bar is fetched it is the previous close while the portfolio is valued at today's prices. AlphaBasis says which
/// it is when Alpha is set: "last_close" (portfolio and index are both as of their last close) or "live_vs_last_close" (the portfolio at prices of a later trading day than the index close, so the alpha includes the day's move of the portfolio only).
/// Alpha compares price returns: the gross XIRR and the index's price index, both before dividends and charges (ADR 0026). Income holds the dividends and costs of the scope and the net returns (null when nothing is recorded): for the whole portfolio or
/// an account (no bucket) it includes the dividends not allocated to a holding and the ledger's costs, for a bucket only its holdings'.
/// </summary>
public sealed record HoldingsSummary(
    ReturnsView Returns,
    int OpenCount,
    int ClosedCount,
    int UnmappedCount,
    int UnpricedCount,
    int DefaultDateLots,
    DateOnly ValuationDate,
    SyncRunView? LastSync,
    decimal? DayChange,
    decimal? DayChangePct,
    int DayChangeExcluded,
    PricesAsOf PricesAsOf,
    decimal? BenchmarkXirr,
    decimal? Alpha,
    string BenchmarkState,
    DateOnly? BenchmarkAsOf,
    string? AlphaBasis,
    IncomeView? Income = null);

/// <summary>The oldest and the newest time of the prices of the open holdings (null when none has a priced time).</summary>
public sealed record PricesAsOf(DateTimeOffset? Oldest, DateTimeOffset? Newest);

/// <summary>
/// A lot of a holding with its own figures: Invested is its cost, Value is its value now (open) or its sale proceeds (closed), Pnl the difference.
/// Status is "open" or "closed"; Source "import", "added" or "manual". Estimated: the sale price is the last price a sync saw.
/// </summary>
public sealed record LotRow(
    Guid Id,
    DateOnly AcquiredOn,
    long Qty,
    decimal UnitCost,
    string Source,
    bool UsesDefaultDate,
    string Status,
    DateOnly? ClosedOn,
    decimal? ClosePrice,
    bool Estimated,
    ReturnsView Returns);

/// <summary>The lots of a holding, open lots first (oldest first), then closed ones.</summary>
public sealed record HoldingLots(Guid HoldingId, string Symbol, decimal LastPrice, DateOnly ValuationDate, IReadOnlyList<LotRow> Lots);

/// <summary>Sets the acquisition date of a lot (between 1990-01-01 and today, India).</summary>
public sealed record LotDateInput(DateOnly? AcquiredOn);

/// <summary>
/// A holding Dhan reports, for the first import in the portfolio settings. Key identifies it in the import request (the ISIN, otherwise "securityId:EXCHANGE").
/// Mapped is false when it is not in the instrument master. AlreadyImported: the account has the holding (open) already.
/// SuggestedDate is the date to pre-fill: the first import date, or the oldest lot's date of an already imported holding.
/// </summary>
public sealed record PreviewRow(
    string Key,
    string Symbol,
    string Name,
    string Exchange,
    long Qty,
    decimal AvgCost,
    decimal LastPrice,
    Guid? InstrumentId,
    string? CanonicalSymbol,
    bool Mapped,
    bool AlreadyImported,
    DateOnly SuggestedDate);

/// <summary>What Dhan holds for the account, nothing saved. DefaultDate is the account's first import date (2025-01-01 until the owner chooses another).</summary>
public sealed record ImportPreview(DateOnly DefaultDate, IReadOnlyList<PreviewRow> Rows);

/// <summary>One holding to import (a key from the preview). AcquiredOn left out means "use the first import date" (the lot is then flagged).</summary>
public sealed record ImportRowInput(string? Key, DateOnly? AcquiredOn);

/// <summary>
/// The refresh settings of an account and its latest run. Mode "off" or "daily"; NextRunAt is null when the mode is off. FirstImportDate is the date holdings
/// get when they are imported without a date of their own; HasHoldings is true once the account has any holding (the first import has happened). Running is
/// true while a sync or import of the account is in progress. The health (ADR 0027): <c>ConsecutiveFailedDays</c> are the days in a row whose last attempt failed (0 when the latest succeeded), <c>LastFailureReason</c> a code (<c>unavailable</c>, <c>rate_limited</c>,
/// <c>interrupted</c>, <c>rejected</c>, <c>token_expired</c>, <c>token_refused</c>, <c>not_connected</c>, <c>unexpected</c>; null while healthy), <c>Attempts</c> the failed attempts of the day of the latest failure and <c>RetryAt</c> when the next quiet retry is due (null when none is pending).
/// </summary>
public sealed record SyncStatusView(
    string Mode,
    string DailyTimeIst,
    DateOnly FirstImportDate,
    bool HasHoldings,
    DateTimeOffset? NextRunAt,
    bool Running,
    SyncRunView? Last,
    int ConsecutiveFailedDays = 0,
    string? LastFailureReason = null,
    int Attempts = 0,
    DateTimeOffset? RetryAt = null);
