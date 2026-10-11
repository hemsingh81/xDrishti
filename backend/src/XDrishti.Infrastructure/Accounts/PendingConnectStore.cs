using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>Pending broker sign-ins in <c>accounts.pending_connects</c>. Using, closing and purging are single SQL statements, so they are atomic and immediate.</summary>
internal sealed class PendingConnectStore(XDrishtiDbContext db) : IPendingConnectStore
{
    public void Add(PendingConnect pending) => db.PendingConnects.Add(pending);

    public async ValueTask<PendingConnect?> FindLatestUnusedAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await db.PendingConnects.AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<int> CountStartedAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await db.PendingConnects.AsNoTracking().CountAsync(x => x.AccountId == accountId && x.CreatedAt >= from && x.CreatedAt < to, cancellationToken);

    public async ValueTask<bool> TryConsumeAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.PendingConnects
            .Where(x => x.Id == id && x.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken) == 1;

    public async ValueTask CloseUnusedAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.PendingConnects
            .Where(x => x.SessionId == sessionId && x.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken);

    public async ValueTask CloseUnusedForAccountAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.PendingConnects
            .Where(x => x.AccountId == accountId && x.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken);

    public async ValueTask DeleteForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.PendingConnects.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

    public async ValueTask PurgeAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken) =>
        await db.PendingConnects.Where(x => x.CreatedAt < createdBefore).ExecuteDeleteAsync(cancellationToken);
}
