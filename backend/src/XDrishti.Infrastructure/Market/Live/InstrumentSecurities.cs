using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Live;

/// <summary>
/// Shares and ETFs of the master list by canonical symbol with their Dhan security id in the NSE equity segment (spec P7-11): not retired, exactly one live instrument per symbol (an ambiguous symbol is left out rather than guessed) and
/// one Dhan id. Read only.
/// </summary>
internal sealed class InstrumentSecurities(XDrishtiDbContext db) : IInstrumentSecurities
{
    private const string DhanBroker = "dhan";

    private const string NseEquitySegment = "NSE_EQ";

    public async ValueTask<IReadOnlyDictionary<string, InstrumentSecurity>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbols);
        var symbols = canonicalSymbols.ToArray();
        if (symbols.Length == 0)
        {
            return new Dictionary<string, InstrumentSecurity>();
        }

        var rows = await (
            from instrument in db.Instruments.AsNoTracking()
            join broker in db.InstrumentBrokerIds.AsNoTracking() on instrument.Id equals broker.InstrumentId
            where instrument.RetiredAt == null && symbols.Contains(instrument.CanonicalSymbol) && broker.Broker == DhanBroker && broker.Segment == NseEquitySegment
            select new { instrument.CanonicalSymbol, InstrumentId = instrument.Id, broker.BrokerId })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.CanonicalSymbol, StringComparer.Ordinal)
            .Where(g => g.Select(r => r.InstrumentId).Distinct().Count() == 1 && g.Select(r => r.BrokerId).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => new InstrumentSecurity(g.Key, g.First().InstrumentId, g.First().BrokerId), StringComparer.Ordinal);
    }
}
