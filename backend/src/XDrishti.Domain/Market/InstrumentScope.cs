namespace XDrishti.Domain.Market;

/// <summary>
/// What the master list keeps (ADR 0010): NSE shares (instrument type ES, any series) and ETFs, NSE indices, NSE stock and index futures.
/// Exchange test symbols (<c>*NSETEST*</c>), debt, BSE, MCX, currency, commodity and options are out of scope.
/// </summary>
public static class InstrumentScope
{
    public const string NseExchange = "NSE";

    /// <summary>Dhan's segment codes in the scrip master.</summary>
    public const string EquitySegment = "E";

    public const string IndexSegment = "I";

    public const string DerivativeSegment = "D";

    /// <summary>The type of a row, or null when the row is out of scope.</summary>
    public static InstrumentType? Classify(string exchange, string segment, string instrument, string instrumentType, params string[] symbolFields)
    {
        ArgumentNullException.ThrowIfNull(symbolFields);
        if (exchange != NseExchange || symbolFields.Any(IsExchangeTestSymbol))
        {
            return null;
        }

        return (segment, instrument, instrumentType) switch
        {
            (EquitySegment, _, "ES") => InstrumentType.Share,
            (EquitySegment, _, "ETF") => InstrumentType.Etf,
            (IndexSegment, "INDEX", _) => InstrumentType.Index,
            (DerivativeSegment, "FUTSTK" or "FUTIDX", _) => InstrumentType.Future,
            _ => null,
        };
    }

    /// <summary>A cheap look at three columns of a row: false means <see cref="Classify"/> cannot keep it (options, currency, debt-like segments), so the row need not be read further.</summary>
    public static bool MightBeInScope(ReadOnlySpan<char> exchange, ReadOnlySpan<char> segment, ReadOnlySpan<char> instrument) =>
        exchange.SequenceEqual(NseExchange)
        && (segment.SequenceEqual(EquitySegment)
            || (segment.SequenceEqual(IndexSegment) && instrument.SequenceEqual("INDEX"))
            || (segment.SequenceEqual(DerivativeSegment) && (instrument.SequenceEqual("FUTSTK") || instrument.SequenceEqual("FUTIDX"))));

    public static bool IsExchangeTestSymbol(string? text) => text is not null && text.Contains("NSETEST", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dhan's name of the exchange segment a row trades in (the broker id is unique within it).</summary>
    public static string BrokerSegment(string segment) => segment switch
    {
        EquitySegment => "NSE_EQ",
        IndexSegment => "IDX_I",
        DerivativeSegment => "NSE_FNO",
        _ => segment,
    };
}
