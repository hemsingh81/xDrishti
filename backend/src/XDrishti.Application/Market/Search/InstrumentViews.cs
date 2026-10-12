namespace XDrishti.Application.Market.Search;

/// <summary>
/// Whether the owner tracks the instrument (every instrument has this block). <c>active</c> = tracked now; <c>since</c> = when tracking last started;
/// <c>deactivatedAt</c> = when it was switched off (history is kept; null while active); <c>dataFrom</c>/<c>dataTo</c>/<c>lastUpdate</c> = the collected
/// history (null until the history service has collected some); <c>backfillRequestedAt</c> = when collecting history was last asked for;
/// <c>retiredButTracked</c> = tracked, but no longer in Dhan's list (a warning, nothing is removed).
/// </summary>
public sealed record TrackedInfo(
    bool Active,
    DateTimeOffset? Since,
    DateTimeOffset? DeactivatedAt,
    DateOnly? DataFrom,
    DateOnly? DataTo,
    DateTimeOffset? LastUpdate,
    DateTimeOffset? BackfillRequestedAt,
    bool RetiredButTracked)
{
    /// <summary>The block of an instrument that was never tracked.</summary>
    public static TrackedInfo Never { get; } = new(false, null, null, null, null, null, null, false);
}

/// <summary>A row of the master list. <c>type</c>: share · etf · index · future.</summary>
public sealed record InstrumentSummary(
    Guid Id,
    string Symbol,
    string Name,
    string DisplayName,
    string Type,
    string? Isin,
    string? Series,
    string? Sector,
    int LotSize,
    decimal? TickSize,
    DateOnly? ExpiryDate,
    bool FnoEligible,
    bool AsmGsm,
    bool Retired,
    TrackedInfo Tracked);

/// <summary>One page of search results (<c>total</c> counts all matches).</summary>
public sealed record InstrumentPage(IReadOnlyList<InstrumentSummary> Items, int Page, int PageSize, int Total);

/// <summary>The id a broker uses for the instrument (Dhan: security id + exchange segment).</summary>
public sealed record BrokerIdView(string Broker, string BrokerId, string Segment);

/// <summary>Everything the master list knows about an instrument.</summary>
public sealed record InstrumentDetail(
    Guid Id,
    string Symbol,
    string Exchange,
    string ExchangeSegment,
    string Type,
    string Name,
    string DisplayName,
    string? Isin,
    string? Series,
    int LotSize,
    decimal? TickSize,
    DateOnly? ExpiryDate,
    string UnderlyingSymbol,
    string? Sector,
    string? SectorSource,
    bool FnoEligible,
    bool AsmGsm,
    string? AsmGsmFlag,
    string? AsmGsmCategory,
    decimal? MtfLeverage,
    long? FreezeQuantity,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RetiredAt,
    IReadOnlyList<BrokerIdView> BrokerIds,
    TrackedInfo Tracked);

/// <summary>A filter value and how many active instruments have it.</summary>
public sealed record FacetCount(string Value, int Count);

/// <summary>
/// Counts of the active (not retired) instruments per filter value. Sectors are counted over shares only (only shares get one);
/// sector "Unclassified" = shares without a known sector, and filtering by it returns exactly those. <c>tracked</c> = instruments tracked now (what
/// <c>?tracked=true</c> lists, retired ones included); <c>trackedRetired</c> = those of them that are no longer in Dhan's list.
/// </summary>
public sealed record InstrumentFacets(
    int Total,
    int Retired,
    IReadOnlyList<FacetCount> Types,
    IReadOnlyList<FacetCount> Sectors,
    IReadOnlyList<FacetCount> Series,
    int FnoEligible,
    int AsmGsm,
    int Tracked,
    int TrackedRetired);
