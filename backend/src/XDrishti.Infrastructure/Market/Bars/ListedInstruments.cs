using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Bars;

internal sealed class ListedInstruments(XDrishtiDbContext db) : IListedInstruments
{
    public async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbols);
        var symbols = canonicalSymbols.ToArray();
        var rows = await db.Instruments.AsNoTracking()
            .Where(x => x.RetiredAt == null && (x.Type == InstrumentTypeCodes.Share || x.Type == InstrumentTypeCodes.Etf) && symbols.Contains(x.CanonicalSymbol))
            .Select(x => new { x.CanonicalSymbol, x.Id })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(x => x.CanonicalSymbol, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(x => x.Id)], StringComparer.Ordinal);
    }
}
