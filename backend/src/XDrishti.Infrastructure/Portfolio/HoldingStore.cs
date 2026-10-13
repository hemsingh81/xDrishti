using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class HoldingStore(XDrishtiDbContext db) : IHoldingStore
{
    public async ValueTask<bool> AnyForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Holdings.AsNoTracking().AnyAsync(x => x.AccountId == accountId, cancellationToken);

    public async ValueTask<IReadOnlyList<Holding>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Holdings.Include(x => x.Lots).Where(x => x.AccountId == accountId).OrderBy(x => x.FirstSeenAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public async ValueTask<Holding?> FindAsync(Guid holdingId, CancellationToken cancellationToken) =>
        await db.Holdings.Include(x => x.Lots).FirstOrDefaultAsync(x => x.Id == holdingId, cancellationToken);

    public void Add(Holding holding) => db.Holdings.Add(holding);

    public void Touch(Holding holding) => db.Entry(holding).Property(x => x.SyncedAt).IsModified = true;
}
