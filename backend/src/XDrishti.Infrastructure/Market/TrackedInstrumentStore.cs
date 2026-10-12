using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

internal sealed class TrackedInstrumentStore(XDrishtiDbContext db) : ITrackedInstrumentStore
{
    public async ValueTask<IReadOnlyList<TrackableInstrument>> FindInstrumentsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        await db.Instruments.AsNoTracking()
            .Where(x => instrumentIds.Contains(x.Id))
            .Select(x => new TrackableInstrument(x.Id, x.CanonicalSymbol, x.RetiredAt != null))
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<TrackedInstrument>> FindAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        await db.TrackedInstruments.Where(x => instrumentIds.Contains(x.Id)).ToListAsync(cancellationToken);

    public void Add(TrackedInstrument tracked) => db.TrackedInstruments.Add(tracked);

    // An unchanged row is not part of a save, so the check on its row version (xmin) would never run: marking one column as modified makes the save
    // update the row (to the value it has) only if it still has the version that was read.
    public void RelyOn(TrackedInstrument tracked) => db.Entry(tracked).Property(x => x.Active).IsModified = true;

    public async ValueTask<int> CountActiveAsync(CancellationToken cancellationToken) =>
        await db.TrackedInstruments.AsNoTracking().CountAsync(x => x.Active, cancellationToken);
}
