using System.Text.RegularExpressions;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Which rows of a tradebook are not equity trades of the NSE and are skipped, with a reason (ADR 0023): derivatives, commodity and currency segments, BSE rows, and anything that is not recognisably equity.
/// Decided from the segment, instrument type and exchange columns and from the shape of the symbol (expiry, strike, <c>FUT</c>, <c>CE</c>, <c>PE</c>). Pure.
/// </summary>
internal static partial class TradeSkips
{
    public const string Derivatives = "derivatives";

    public const string Commodity = "commodity";

    public const string Currency = "currency";

    public const string Bse = "bse";

    public const string NonEquity = "non_equity";

    /// <summary>The reason a segment, instrument type or exchange cell says the row is not an NSE equity trade; null when it says equity (or nothing).</summary>
    public static string? ClassifyCell(string? cell)
    {
        var text = TradeColumns.Normalise(cell);
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Contains("bse", StringComparison.Ordinal))
        {
            return Bse;
        }

        if (text is "fo" or "fno" or "nfo" or "bfo" or "ce" or "pe" || Has(text, "fut", "opt", "deriv", "fno"))
        {
            return Derivatives;
        }

        if (Has(text, "comm", "mcx", "ncdex") || text == "com")
        {
            return Commodity;
        }

        if (Has(text, "curr", "cds", "forex") || text is "fx" or "bcd")
        {
            return Currency;
        }

        if (text is "e" or "cm" or "nse" or "etf" or "stock" or "stocks" or "delivery" || Has(text, "eq", "cash", "capital"))
        {
            return null;
        }

        return NonEquity;
    }

    /// <summary>Derivatives are recognised by the symbol alone too: <c>NIFTY24MARFUT</c>, <c>RELIANCE24MAR2500CE</c>, <c>NIFTY2431422000CE</c>.</summary>
    public static bool LooksLikeDerivative(string symbol) => symbol.Length is > 0 and <= TradeCells.MaxCellLength && DerivativeSymbol().IsMatch(symbol);

    private static bool Has(string text, params string[] parts) => parts.Any(p => text.Contains(p, StringComparison.Ordinal));

    [GeneratedRegex(@"(FUT|FUTIDX|FUTSTK|OPTIDX|OPTSTK)$|\d{2}(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)|\d(CE|PE)$|\s(CE|PE|FUT)(\s|$)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex DerivativeSymbol();
}
