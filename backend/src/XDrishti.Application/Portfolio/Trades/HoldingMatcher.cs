using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>How a security of a tradebook relates to the account's holdings: none, exactly one, or more than one (not applied).</summary>
internal enum MatchKind
{
    None,
    One,
    Ambiguous,
}

internal readonly record struct HoldingMatch(MatchKind Kind, Holding? Holding)
{
    public static HoldingMatch None => new(MatchKind.None, null);

    public static HoldingMatch Ambiguous => new(MatchKind.Ambiguous, null);
}

/// <summary>
/// Finds the open holding of an account that a trade belongs to (ADR 0023): by ISIN first; when no holding has the trade's ISIN (or the trade has none), by trading symbol — upper case, without <c>NSE:</c> and <c>-EQ</c> — but a symbol
/// never joins two different ISINs. More than one candidate is ambiguous (an NSE and a BSE row of one ISIN) and is not applied. Closed holdings are not matched. Pure.
/// </summary>
internal sealed class HoldingMatcher
{
    private readonly Dictionary<string, List<Holding>> _byIsin = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<Holding>> _bySymbol = new(StringComparer.Ordinal);

    public HoldingMatcher(IEnumerable<Holding> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        foreach (var holding in holdings.Where(h => h.Status == HoldingStatus.Open))
        {
            if (holding.Isin is { } isin)
            {
                Add(_byIsin, isin, holding);
            }

            var symbol = TradeRules.NormaliseSymbol(holding.TradingSymbol);
            if (symbol.Length > 0)
            {
                Add(_bySymbol, symbol, holding);
            }
        }
    }

    public HoldingMatch Find(string? isin, string? symbol)
    {
        var code = TradeRules.NormaliseIsin(isin);
        if (code is not null && _byIsin.TryGetValue(code, out var byIsin))
        {
            return One(byIsin);
        }

        var name = TradeRules.NormaliseSymbol(symbol);
        if (name.Length > 0 && _bySymbol.TryGetValue(name, out var bySymbol))
        {
            var compatible = bySymbol.Where(h => code is null || h.Isin is null).ToList();
            return One(compatible);
        }

        return HoldingMatch.None;
    }

    private static HoldingMatch One(List<Holding> candidates) => candidates.Count switch
    {
        0 => HoldingMatch.None,
        1 => new HoldingMatch(MatchKind.One, candidates[0]),
        _ => HoldingMatch.Ambiguous,
    };

    private static void Add(Dictionary<string, List<Holding>> index, string key, Holding holding)
    {
        if (!index.TryGetValue(key, out var list))
        {
            index[key] = list = [];
        }

        list.Add(holding);
    }
}
