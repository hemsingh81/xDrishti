using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>The dated lot sizes (<c>market.fo_lot_sizes</c>, insert-only for the runtime role). One SaveChanges is one transaction: the lot rows, the fetch row and the source's state together or not at all.</summary>
internal sealed class FoLotSizeStore(XDrishtiDbContext db, BasketSourceStore sources) : IFoLotSizeStore
{
    public async ValueTask<IReadOnlyDictionary<Guid, int>> LatestAsync(CancellationToken cancellationToken)
    {
        var rows = await db.FoLotSizes.AsNoTracking().Select(l => new { l.InstrumentId, l.EffectiveFrom, l.LotSize }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.InstrumentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EffectiveFrom).First().LotSize);
    }

    public async ValueTask ApplyAsync(SourceUpdate source, FetchRecord fetch, DateOnly effectiveFrom, IReadOnlyList<LotWrite> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var existing = (await db.FoLotSizes.AsNoTracking().Where(l => l.EffectiveFrom == effectiveFrom).Select(l => l.InstrumentId).ToListAsync(cancellationToken)).ToHashSet();
        sources.Stage(source, fetch);
        foreach (var row in rows.Where(r => !existing.Contains(r.InstrumentId)))
        {
            db.FoLotSizes.Add(new FoLotSizeRecord { InstrumentId = row.InstrumentId, EffectiveFrom = effectiveFrom, LotSize = row.LotSize, ExpiryLabel = row.ExpiryLabel, FetchId = fetch.Id });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
