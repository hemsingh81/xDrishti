using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>What the trades read from the market module (ADR 0023): the NSE shares and ETFs of the master list by ISIN or ticker, and the applied corporate actions of instruments. Reads only, not tracked, one query each.</summary>
internal sealed class TradeReferenceData(XDrishtiDbContext db) : ITradeReferenceData
{
    private const string Nse = "NSE";

    private const string ExchangePrefix = "NSE:";

    private const string DhanBroker = "dhan";

    private const string NseEquitySegment = "NSE_EQ";

    public async ValueTask<IReadOnlyList<TradeInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isins);
        ArgumentNullException.ThrowIfNull(symbols);
        if (isins.Count == 0 && symbols.Count == 0)
        {
            return [];
        }

        var codes = isins.ToArray();
        var tickers = symbols.Select(s => ExchangePrefix + s).ToArray();
        var rows = await db.Instruments.AsNoTracking()
            .Where(i => i.Exchange == Nse && (i.Type == InstrumentTypeCodes.Share || i.Type == InstrumentTypeCodes.Etf)
                && ((i.Isin != null && codes.Contains(i.Isin)) || tickers.Contains(i.CanonicalSymbol)))
            .Select(i => new { i.Id, i.CanonicalSymbol, i.Isin, i.Type })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new TradeInstrument(r.Id, Ticker(r.CanonicalSymbol), r.Isin, r.Type))];
    }

    public async ValueTask<IReadOnlyDictionary<string, TradeInstrument>> FindInstrumentsByDhanIdsAsync(IReadOnlyCollection<string> securityIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityIds);
        if (securityIds.Count == 0)
        {
            return new Dictionary<string, TradeInstrument>();
        }

        var ids = securityIds.ToArray();
        var rows = await (
            from b in db.InstrumentBrokerIds.AsNoTracking()
            join i in db.Instruments.AsNoTracking() on b.InstrumentId equals i.Id
            where b.Broker == DhanBroker && b.Segment == NseEquitySegment && ids.Contains(b.BrokerId)
                && i.Exchange == Nse && (i.Type == InstrumentTypeCodes.Share || i.Type == InstrumentTypeCodes.Etf)
            select new { b.BrokerId, i.Id, i.CanonicalSymbol, i.Isin, i.Type })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.BrokerId, r => new TradeInstrument(r.Id, Ticker(r.CanonicalSymbol), r.Isin, r.Type), StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>>> ListAppliedActionsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<AppliedAction>>();
        }

        var rows = await db.CorporateActions.AsNoTracking()
            .Where(x => ids.Contains(x.InstrumentId) && x.Status == CorporateActionStatus.Applied)
            .OrderBy(x => x.ExDate).ThenBy(x => x.Kind)
            .Select(x => new { x.InstrumentId, Action = new AppliedAction(x.ExDate, x.Kind, x.Numerator, x.Denominator, x.Factor) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.InstrumentId).ToDictionary(g => g.Key, g => (IReadOnlyList<AppliedAction>)[.. g.Select(r => r.Action)]);
    }

    private static string Ticker(string canonicalSymbol) =>
        canonicalSymbol.StartsWith(ExchangePrefix, StringComparison.Ordinal) ? canonicalSymbol[ExchangePrefix.Length..] : canonicalSymbol;
}
