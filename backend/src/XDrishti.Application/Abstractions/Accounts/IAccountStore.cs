using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>
/// Persistence port for broker accounts (archived accounts are never returned except by <see cref="ExistsAsync"/>).
/// Changes to tracked accounts are saved by <see cref="IAccountUnitOfWork"/>.
/// </summary>
public interface IAccountStore
{
    /// <summary>Active accounts in the order they were created. Not tracked: for reading only.</summary>
    ValueTask<IReadOnlyList<BrokerAccount>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>One active account for reading only; null when it does not exist or is archived.</summary>
    ValueTask<BrokerAccount?> FindReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One active account, tracked so that changes can be saved; null when it does not exist or is archived.</summary>
    ValueTask<BrokerAccount?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The active primary Data account (tracked); null when there is none.</summary>
    ValueTask<BrokerAccount?> FindPrimaryDataAsync(CancellationToken cancellationToken);

    /// <summary>The active standby Data account (tracked); null when there is none.</summary>
    ValueTask<BrokerAccount?> FindStandbyDataAsync(CancellationToken cancellationToken);

    /// <summary>True for any account that was ever created, archived ones included (their audit trail stays readable).</summary>
    ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    void Add(BrokerAccount account);
}
