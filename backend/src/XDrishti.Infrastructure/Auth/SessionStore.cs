using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Auth;

internal sealed class SessionStore(XDrishtiDbContext db) : ISessionStore
{
    // One query via the unique token_hash index; both entities are tracked so the caller can update them.
    public async ValueTask<SessionWithOwner?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var row = await (
            from s in db.AuthSessions
            join o in db.OwnerAccounts on s.OwnerId equals o.Id
            where s.TokenHash == tokenHash
            select new { Session = s, Owner = o }).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new SessionWithOwner(row.Session, row.Owner);
    }

    public async ValueTask<SessionWithOwner?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var row = await (
            from s in db.AuthSessions
            join o in db.OwnerAccounts on s.OwnerId equals o.Id
            where s.Id == sessionId
            select new { Session = s, Owner = o }).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new SessionWithOwner(row.Session, row.Owner);
    }

    public async ValueTask<IReadOnlyList<AuthSession>> ListNotRevokedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.AuthSessions.Where(x => x.OwnerId == ownerId && x.RevokedAt == null).ToListAsync(cancellationToken);

    public void Add(AuthSession session) => db.AuthSessions.Add(session);
}
