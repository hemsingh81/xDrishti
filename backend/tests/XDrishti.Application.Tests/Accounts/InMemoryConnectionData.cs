using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

/// <summary>In-memory broker connections and pending sign-ins; entities are shared by reference like a tracked DbContext.</summary>
internal sealed class InMemoryConnectionData : IBrokerConnectionStore, IPendingConnectStore
{
    public List<BrokerConnection> Connections { get; } = [];

    public List<PendingConnect> Pending { get; } = [];

    public ValueTask<BrokerConnection?> FindAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Connections.FirstOrDefault(c => c.AccountId == accountId));

    public ValueTask<IReadOnlyDictionary<Guid, BrokerConnection>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, BrokerConnection>>(Connections.Where(c => accountIds.Contains(c.AccountId)).ToDictionary(c => c.AccountId));

    public void Add(BrokerConnection connection) => Connections.Add(connection);

    public void Add(PendingConnect pending) => Pending.Add(pending);

    public ValueTask<PendingConnect?> FindLatestUnusedAsync(Guid sessionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Pending.Where(p => p.SessionId == sessionId && p.ConsumedAt is null).OrderByDescending(p => p.CreatedAt).FirstOrDefault());

    public ValueTask<int> CountStartedAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Pending.Count(p => p.AccountId == accountId && p.CreatedAt >= from && p.CreatedAt < to));

    public ValueTask<bool> TryConsumeAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Pending.FirstOrDefault(p => p.Id == id)?.Consume(now) ?? false);

    public ValueTask CloseUnusedAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var pending in Pending.Where(p => p.SessionId == sessionId))
        {
            pending.Consume(now);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CloseUnusedForAccountAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var pending in Pending.Where(p => p.AccountId == accountId))
        {
            pending.Consume(now);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteForAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Pending.RemoveAll(p => p.AccountId == accountId);
        return ValueTask.CompletedTask;
    }

    public ValueTask PurgeAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken)
    {
        Pending.RemoveAll(p => p.CreatedAt < createdBefore);
        return ValueTask.CompletedTask;
    }
}
