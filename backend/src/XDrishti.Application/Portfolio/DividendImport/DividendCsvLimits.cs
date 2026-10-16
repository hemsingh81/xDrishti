using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>Limits of a dividend file (ADR 0028): the same size, data rows and listed bad rows as a tradebook, and the lists a report keeps short.</summary>
public static class DividendCsvLimits
{
    public const int MaxBytes = TradeCsvLimits.MaxBytes;

    public const int MaxRows = TradeCsvLimits.MaxRows;

    public const int MaxListedBad = TradeCsvLimits.MaxListedBad;

    /// <summary>The most identically repeated payments a report lists; they are all counted.</summary>
    public const int MaxListedRepeats = 200;

    /// <summary>The most lines listed for one repeated payment.</summary>
    public const int MaxLinesPerRepeat = 10;

    /// <summary>The most choices an import takes.</summary>
    public const int MaxChoices = 2_000;
}
