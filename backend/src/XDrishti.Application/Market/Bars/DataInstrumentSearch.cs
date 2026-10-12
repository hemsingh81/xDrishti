namespace XDrishti.Application.Market.Bars;

public enum DataInstrumentStatus
{
    Current,
    Behind,
    Empty,
}

public enum DataSortField
{
    Symbol,
    Name,
    FirstBar,
    LastBar,
    Bars,
}

/// <summary>The coverage list as received (query string values, unchecked).</summary>
public sealed record DataInstrumentRequest(string? Q, string? Status, string? Sort, int? Page, int? PageSize);

/// <summary>A checked coverage search: text matches symbol and name (substring, case-insensitive); the status filters; the order is stable.</summary>
public sealed record DataInstrumentSearch(string? Text, DataInstrumentStatus? Status, DataSortField SortField, bool Descending, int Page, int PageSize)
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxTextLength = 100;

    public const int MaxPage = 100_000;
}
