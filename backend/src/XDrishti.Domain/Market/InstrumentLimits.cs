namespace XDrishti.Domain.Market;

/// <summary>Largest values the master list stores (the column widths); a row beyond them is skipped with a warning instead of failing the whole save.</summary>
public static class InstrumentLimits
{
    public const int BrokerId = 32;

    public const int Series = 8;

    public const int Isin = 16;

    public const int AsmGsmFlag = 8;

    public const int AsmGsmCategory = 16;

    public const int Name = 200;

    public const int DisplayName = 200;

    /// <summary>Under the column width of 100: the canonical symbol adds <c>NSE:</c> and, for futures, <c>-YYYY-MM-FUT</c>.</summary>
    public const int UnderlyingSymbol = 80;

    public const int CanonicalSymbol = 100;

    /// <summary>Numeric(14,4) and numeric(12,6): larger values do not fit.</summary>
    public const decimal TickSizeExclusiveMax = 1_000_000_000m;

    public const decimal MtfLeverageExclusiveMax = 1_000_000m;
}
