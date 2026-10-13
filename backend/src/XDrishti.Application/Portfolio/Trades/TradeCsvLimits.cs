namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Limits of a tradebook file (ADR 0023): the size, the data rows, the bad rows that are listed, the longest trade id and the unmatched securities a report lists.</summary>
public static class TradeCsvLimits
{
    public const int MaxBytes = 10 * 1024 * 1024;

    public const int MaxRows = 20_000;

    public const int MaxListedBad = 200;

    public const int MaxTradeIdLength = 64;

    /// <summary>The most unmatched securities (neither held nor an NSE share/ETF of the master) a report lists; they are all counted.</summary>
    public const int MaxListedUnmatched = 200;
}
