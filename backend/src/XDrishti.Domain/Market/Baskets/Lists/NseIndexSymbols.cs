namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>
/// The index tickers of <c>fo_mktlots.csv</c> (BK-05b) and the master's canonical symbol of each (<c>NSE:{TICKER}</c>, as the master's index rows and the option underlyings are named). <c>NIFTYNXT50</c> has no verified canonical symbol
/// (the master would name it after its display name), so it maps to nothing and is counted as an unmatched index row rather than guessed. No I/O.
/// </summary>
public static class NseIndexSymbols
{
    private static readonly Dictionary<string, string?> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NIFTY"] = "NSE:NIFTY",
        ["BANKNIFTY"] = "NSE:BANKNIFTY",
        ["FINNIFTY"] = "NSE:FINNIFTY",
        ["MIDCPNIFTY"] = "NSE:MIDCPNIFTY",
        ["NIFTYNXT50"] = null,
    };

    /// <summary>Whether the ticker is one of the file's indices.</summary>
    public static bool IsIndex(string symbol) => Map.ContainsKey(symbol);

    /// <summary>The canonical symbol of a file index ticker; null when it is not mapped.</summary>
    public static string? Canonical(string symbol) => Map.GetValueOrDefault(symbol);
}
