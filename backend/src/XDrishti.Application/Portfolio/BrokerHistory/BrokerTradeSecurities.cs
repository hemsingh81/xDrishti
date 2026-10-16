using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>A broker trade with the security it was resolved to: the ticker to store (empty when only the ISIN is known, like a tradebook line with an ISIN and no symbol), the ISIN, and the master-list instrument when one was found.</summary>
internal sealed record ResolvedBrokerTrade(BrokerTrade Trade, string Symbol, string? Isin, Guid? InstrumentId)
{
    /// <summary>False when neither a ticker nor an ISIN is known: the row cannot be told from any other security and is not stored.</summary>
    public bool IsIdentified => Symbol.Length > 0 || Isin is not null;
}

/// <summary>
/// Decides which security each trade of a Dhan window is (ADR 0025). Dhan's <c>tradingSymbol</c> can be missing - its <c>customSymbol</c> is a company name and is never used as a ticker - so a row without one is resolved from the instrument master:
/// by Dhan's security id (the NSE equity segment's id, the exact key), else by ISIN; the ticker of the share found is stored, with the ISIN of the row (or, for a row without one, of the share). A row that no instrument matches keeps its ISIN
/// and an empty ticker - the same convention as a tradebook CSV line with an ISIN and no symbol - and still matches its holding by ISIN. A row with neither a ticker nor an ISIN after all this is not identified (the caller counts it as
/// <see cref="BrokerSkipReasons.NoSymbol"/>). A row with a ticker keeps it, and its ISIN is stored beside it. Two reads of the master (shares by ISIN or ticker, Dhan ids), whatever the number of rows.
/// </summary>
internal static class BrokerTradeSecurities
{
    public static async ValueTask<IReadOnlyList<ResolvedBrokerTrade>> ResolveAsync(ITradeReferenceData reference, IReadOnlyList<BrokerTrade> trades, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(trades);
        if (trades.Count == 0)
        {
            return [];
        }

        var isins = trades.Select(t => TradeRules.NormaliseIsin(t.Isin)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var symbols = trades.Select(t => TradeRules.NormaliseSymbol(t.Symbol)).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var master = new TradeInstruments(await reference.FindInstrumentsAsync(isins, symbols, cancellationToken));

        var unnamed = trades.Where(t => TradeRules.NormaliseSymbol(t.Symbol).Length == 0).Select(t => t.SecurityId?.Trim()).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).OfType<string>().ToArray();
        var byDhanId = unnamed.Length == 0 ? new Dictionary<string, TradeInstrument>() : await reference.FindInstrumentsByDhanIdsAsync(unnamed, cancellationToken);

        var resolved = new List<ResolvedBrokerTrade>(trades.Count);
        foreach (var trade in trades)
        {
            var isin = TradeRules.NormaliseIsin(trade.Isin);
            var symbol = TradeRules.NormaliseSymbol(trade.Symbol);
            if (symbol.Length > 0)
            {
                resolved.Add(new ResolvedBrokerTrade(trade, symbol, isin, master.Find(isin, symbol)?.Id));
                continue;
            }

            var found = trade.SecurityId?.Trim() is { Length: > 0 } id && byDhanId.TryGetValue(id, out var byId) ? byId : master.Find(isin, null);
            resolved.Add(new ResolvedBrokerTrade(trade, found?.Symbol ?? string.Empty, isin ?? TradeRules.NormaliseIsin(found?.Isin), found?.Id));
        }

        return resolved;
    }
}
