namespace XDrishti.Application.Market.Search;

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
    bool Retired);

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
    IReadOnlyList<BrokerIdView> BrokerIds);

/// <summary>A filter value and how many active instruments have it.</summary>
public sealed record FacetCount(string Value, int Count);

/// <summary>
/// Counts of the active (not retired) instruments per filter value. Sectors are counted over shares only (only shares get one);
/// sector "Unclassified" = shares without a known sector, and filtering by it returns exactly those.
/// </summary>
public sealed record InstrumentFacets(
    int Total,
    int Retired,
    IReadOnlyList<FacetCount> Types,
    IReadOnlyList<FacetCount> Sectors,
    IReadOnlyList<FacetCount> Series,
    int FnoEligible,
    int AsmGsm);
