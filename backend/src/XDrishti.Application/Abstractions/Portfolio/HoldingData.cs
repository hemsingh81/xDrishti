namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A lot as read for calculations and display. Source is import, added or manual.</summary>
public sealed record LotData(
    Guid Id,
    DateOnly AcquiredOn,
    long Qty,
    decimal UnitCost,
    string Source,
    bool UsesDefaultDate,
    DateOnly? ClosedOn,
    decimal? ClosePrice,
    bool Estimated)
{
    public bool IsOpen => ClosedOn is null;
}

/// <summary>
/// A holding as read for calculations and display: its identity, names, the broker's figures and all its lots. Status is open or closed. <see cref="PreviousClose"/> is the previous session's close as the last quote
/// reported it (null: none yet); <see cref="PriceSource"/> says whether <see cref="LastPrice"/> came from the sync (<c>sync</c>) or from a quote (<c>quote</c>), at <see cref="LastPriceAt"/>.
/// </summary>
public sealed record HoldingData(
    Guid Id,
    Guid AccountId,
    string AccountName,
    Guid BucketId,
    string BucketName,
    Guid? InstrumentId,
    string? CanonicalSymbol,
    string Exchange,
    string SecurityId,
    string? Isin,
    string TradingSymbol,
    string Name,
    long Qty,
    decimal AvgCost,
    decimal LastPrice,
    DateTimeOffset? LastPriceAt,
    string Status,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset SyncedAt,
    IReadOnlyList<LotData> Lots,
    decimal? PreviousClose = null,
    string PriceSource = HoldingData.SyncSource)
{
    /// <summary>The price came from the broker's holdings list at the last sync.</summary>
    public const string SyncSource = "sync";

    /// <summary>The price came from a market quote (ADR 0019).</summary>
    public const string QuoteSource = "quote";

    public bool IsOpen => Status == "open";

    /// <summary>
    /// False for a holding that is still held but has no usable price (a last price of 0, for example a new listing whose last traded price Dhan reports as 0):
    /// its worth is unknown, so no value, profit or XIRR is computed for it (it is not a total loss).
    /// </summary>
    public bool IsPriced => LastPrice > 0m || Lots.All(l => !l.IsOpen);
}

/// <summary>
/// Which holdings to read. Without <see cref="AccountId"/> only accounts that are active and have the Portfolio role are read; with
/// <see cref="TotalsOnly"/> also only those that count in totals. <see cref="Status"/> "open" or "closed" (null: both).
/// </summary>
public sealed record HoldingFilter(Guid? AccountId, Guid? BucketId, string? Status, string? Text, bool TotalsOnly = false);
