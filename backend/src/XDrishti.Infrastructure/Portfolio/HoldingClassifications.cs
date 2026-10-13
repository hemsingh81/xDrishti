using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class HoldingClassifications(XDrishtiDbContext db) : IHoldingClassifications
{
    public async ValueTask<IReadOnlyDictionary<Guid, string>> SectorsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await db.Instruments.AsNoTracking()
            .Where(i => ids.Contains(i.Id) && i.Sector != null)
            .Select(i => new { i.Id, i.Sector })
            .ToListAsync(cancellationToken);
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Sector))
            .ToDictionary(r => r.Id, r => r.Sector!.Trim());
    }

    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>> BasketsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<BasketRef>>();
        }

        var rows = await (
            from m in db.BasketMembers.AsNoTracking().Where(m => m.ValidTo == null && ids.Contains(m.InstrumentId))
            join b in db.Baskets.AsNoTracking().Where(b => b.ArchivedAt == null) on m.BasketId equals b.Id
            orderby b.Position, b.CreatedAt, b.Id
            select new { m.InstrumentId, b.Id, b.Name }).ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.InstrumentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BasketRef>)[.. g.Select(r => new BasketRef(r.Id, r.Name))]);
    }
}
