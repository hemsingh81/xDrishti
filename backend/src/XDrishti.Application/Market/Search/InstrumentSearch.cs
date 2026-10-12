using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Search;

/// <summary>The search as received (query string values, unchecked).</summary>
public sealed record InstrumentSearchRequest(
    string? Q,
    string? Type,
    string? Sector,
    bool? Fno,
    bool? AsmGsm,
    string? Series,
    bool? IncludeRetired,
    string? Sort,
    int? Page,
    int? PageSize,
    bool? Tracked = null);

public enum InstrumentSortField
{
    Symbol,
    Name,
    Type,
    Series,
    Sector,
    Lot,
    Tick,
}

/// <summary>
/// A checked search: text matches symbol, name, display name and ISIN (substring, case-insensitive); the filters combine with AND. <see cref="Unclassified"/> as the sector means shares without a known sector (only shares get sectors).
/// </summary>
public sealed record InstrumentSearch(
    string? Text,
    InstrumentType? Type,
    string? Sector,
    bool? Fno,
    bool? AsmGsm,
    string? Series,
    bool IncludeRetired,
    InstrumentSortField SortField,
    bool Descending,
    int Page,
    int PageSize,
    bool? Tracked)
{
    public const string Unclassified = "Unclassified";

    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 50;

    public const int MaxTextLength = 100;

    public const int MaxSectorLength = 100;

    public const int MaxSeriesLength = 8;

    /// <summary>Deepest page that can be asked for (offset paging gets slow, and nobody reads page 100,000).</summary>
    public const int MaxPage = 100_000;
}
