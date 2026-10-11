using System.Globalization;

namespace XDrishti.Domain.Market;

/// <summary>Canonical symbols of the master list (ADR 0010): the exchange-qualified key every other module uses, independent of any broker's ids.</summary>
public static class CanonicalSymbol
{
    public const string Exchange = "NSE";

    /// <summary>A share or ETF: <c>NSE:{ticker}</c>, for example <c>NSE:RELIANCE</c>.</summary>
    public static string ForListed(string tickerSymbol) => $"{Exchange}:{tickerSymbol.Trim()}";

    /// <summary>An index: <c>NSE:{NAME}</c>, upper-case, runs of whitespace as one <c>_</c> (<c>NSE:NIFTY_MIDCAP_150</c>).</summary>
    public static string ForIndex(string symbolName) =>
        $"{Exchange}:{string.Join('_', symbolName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant()}";

    /// <summary>A future: <c>NSE:{underlying}-{YYYY}-{MM}-FUT</c>, from the contract's expiry month (<c>NSE:RELIANCE-2026-10-FUT</c>).</summary>
    public static string ForFuture(string underlyingSymbol, DateOnly expiry) =>
        string.Create(CultureInfo.InvariantCulture, $"{Exchange}:{underlyingSymbol.Trim()}-{expiry.Year:0000}-{expiry.Month:00}-FUT");
}
