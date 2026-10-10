using XDrishti.Application.Abstractions.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Application.Tests.Auth;

/// <summary>One in-memory stand-in for all auth persistence ports; entities are shared by reference like a tracked DbContext.</summary>
internal sealed class InMemoryAuthData : IOwnerAccountStore, ISessionStore, IAuditLog, IAuthUnitOfWork
{
    public List<OwnerAccount> Owners { get; } = [];

    public List<AuthSession> Sessions { get; } = [];

    public List<AuditEvent> Events { get; } = [];

    public int SaveCount { get; private set; }

    /// <summary>
    /// Each entry simulates a concurrent request that wins the next <see cref="TrySaveChangesAsync"/>: what the caller collected is
    /// discarded (as the real unit of work rolls back), the action then changes the stored state, and the save reports a conflict.
    /// </summary>
    public Queue<Action> ConcurrentWrites { get; } = new();

    public int ConflictCount { get; private set; }

    private int _committedSessions;
    private int _committedEvents;

    public ValueTask<OwnerAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Owners.FirstOrDefault(o => o.Email == normalizedEmail));

    public ValueTask<OwnerAccount?> FindSoleOwnerAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Owners.FirstOrDefault());

    public void Add(OwnerAccount owner) => Owners.Add(owner);

    public ValueTask<SessionWithOwner?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Join(Sessions.FirstOrDefault(s => s.TokenHash == tokenHash)));

    public ValueTask<SessionWithOwner?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Join(Sessions.FirstOrDefault(s => s.Id == sessionId)));

    public ValueTask<IReadOnlyList<AuthSession>> ListNotRevokedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AuthSession>>([.. Sessions.Where(s => s.OwnerId == ownerId && s.RevokedAt is null)]);

    public void Add(AuthSession session) => Sessions.Add(session);

    public void Record(AuditEvent auditEvent) => Events.Add(auditEvent);

    public ValueTask<IReadOnlyList<AuditEvent>> ListLatestAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AuditEvent>>([.. Events.OrderByDescending(e => e.At).Take(take)]);

    public ValueTask SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        Commit();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        if (!ConcurrentWrites.TryDequeue(out var concurrentWrite))
        {
            Commit();
            return ValueTask.FromResult(true);
        }

        ConflictCount++;
        Sessions.RemoveRange(_committedSessions, Sessions.Count - _committedSessions);
        Events.RemoveRange(_committedEvents, Events.Count - _committedEvents);
        concurrentWrite();
        return ValueTask.FromResult(false);
    }

    private void Commit()
    {
        _committedSessions = Sessions.Count;
        _committedEvents = Events.Count;
    }

    private SessionWithOwner? Join(AuthSession? session) =>
        session is null ? null : new SessionWithOwner(session, Owners.Single(o => o.Id == session.OwnerId));
}
