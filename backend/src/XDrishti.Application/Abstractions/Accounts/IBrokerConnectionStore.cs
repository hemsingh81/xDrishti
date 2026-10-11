using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Persistence port for broker connections (one per account; none = not connected). Changes are saved by <see cref="IAccountUnitOfWork"/>.</summary>
public interface IBrokerConnectionStore
{
    /// <summary>The account's connection, tracked so that changes can be saved; null when it never had one.</summary>
    ValueTask<BrokerConnection?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The connections of the accounts, for reading only. Accounts without one are absent.</summary>
    ValueTask<IReadOnlyDictionary<Guid, BrokerConnection>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    void Add(BrokerConnection connection);
}
