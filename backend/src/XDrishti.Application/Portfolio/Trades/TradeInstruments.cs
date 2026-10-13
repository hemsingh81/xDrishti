using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The NSE shares and ETFs of the master list that a set of tradebook securities map to, by ISIN first and then by ticker. Pure.</summary>
internal sealed class TradeInstruments
{
    public static readonly TradeInstruments Empty = new([]);

    private readonly Dictionary<string, TradeInstrument> _byIsin = new(StringComparer.Ordinal);

    private readonly Dictionary<string, TradeInstrument> _bySymbol = new(StringComparer.Ordinal);

    public TradeInstruments(IEnumerable<TradeInstrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        foreach (var instrument in instruments.OrderBy(i => i.Symbol, StringComparer.Ordinal).ThenBy(i => i.Id))
        {
            if (instrument.Isin is { Length: > 0 } isin)
            {
                _byIsin.TryAdd(isin, instrument);
            }

            _bySymbol.TryAdd(instrument.Symbol, instrument);
        }
    }

    public TradeInstrument? Find(string? isin, string? symbol)
    {
        var code = TradeRules.NormaliseIsin(isin);
        if (code is not null && _byIsin.TryGetValue(code, out var byIsin))
        {
            return byIsin;
        }

        var name = TradeRules.NormaliseSymbol(symbol);
        return name.Length > 0 && _bySymbol.TryGetValue(name, out var bySymbol) && (code is null || bySymbol.Isin is null) ? bySymbol : null;
    }
}
