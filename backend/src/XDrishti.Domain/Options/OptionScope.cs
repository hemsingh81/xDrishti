using XDrishti.Domain.Market;

namespace XDrishti.Domain.Options;

/// <summary>
/// Which rows of Dhan's scrip master are option contracts the option archive keeps (spec P14-01, ADR 0175): NSE derivative rows (segment <c>D</c>) of instrument
/// <c>OPTIDX</c> (index options) or <c>OPTSTK</c> (stock options), never an exchange test symbol. BSE, currency and commodity options are out. Whether the
/// underlying is listed is the job of <see cref="OptionUnderlyings"/>. <see cref="InstrumentScope"/> is unchanged: options never enter the master list.
/// </summary>
public static class OptionScope
{
    public const string IndexOption = "OPTIDX";

    public const string StockOption = "OPTSTK";

    /// <summary>A cheap look at three columns: true when the row may be an option contract this scope keeps.</summary>
    public static bool MightBeOption(ReadOnlySpan<char> exchange, ReadOnlySpan<char> segment, ReadOnlySpan<char> instrument) =>
        exchange.SequenceEqual(InstrumentScope.NseExchange)
        && segment.SequenceEqual(InstrumentScope.DerivativeSegment)
        && (instrument.SequenceEqual(IndexOption) || instrument.SequenceEqual(StockOption));

    /// <summary>True for an NSE index or stock option row that is not an exchange test symbol.</summary>
    public static bool IsOption(string exchange, string segment, string instrument, params string[] symbolFields)
    {
        ArgumentNullException.ThrowIfNull(symbolFields);
        return MightBeOption(exchange, segment, instrument) && !symbolFields.Any(InstrumentScope.IsExchangeTestSymbol);
    }

    /// <summary>
    /// The canonical symbol of an option's underlying in the master list: an index option names an index (<see cref="CanonicalSymbol.ForIndex"/>), a stock
    /// option a share (<see cref="CanonicalSymbol.ForListed"/>). Dhan's underlying security id is not used: for an index option it is the exchange's index
    /// token (26000 for NIFTY), not the id of the index row (13).
    /// </summary>
    public static string UnderlyingSymbol(string instrument, string underlyingTicker) =>
        instrument == IndexOption ? CanonicalSymbol.ForIndex(underlyingTicker) : CanonicalSymbol.ForListed(underlyingTicker);
}
