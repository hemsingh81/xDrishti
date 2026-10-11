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
    ReturnsView Returns);

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
/// </summary>
public sealed record HoldingsSummary(
    ReturnsView Returns,
    int OpenCount,
    int ClosedCount,
    int UnmappedCount,
    int UnpricedCount,
    int DefaultDateLots,
    DateOnly ValuationDate,
    SyncRunView? LastSync);

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
/// true while a sync or import of the account is in progress.
/// </summary>
public sealed record SyncStatusView(
    string Mode,
    string DailyTimeIst,
    DateOnly FirstImportDate,
    bool HasHoldings,
    DateTimeOffset? NextRunAt,
    bool Running,
    SyncRunView? Last);
