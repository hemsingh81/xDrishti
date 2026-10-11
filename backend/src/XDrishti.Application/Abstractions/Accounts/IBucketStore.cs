using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Persistence port for portfolio buckets. New and changed buckets are saved by <see cref="IAccountUnitOfWork"/>.</summary>
public interface IBucketStore
{
    /// <summary>Buckets that are not archived (account defaults and custom ones), oldest first. Not tracked: for reading only.</summary>
    ValueTask<IReadOnlyList<PortfolioBucket>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One bucket by name (looked up ignoring case, archived ones included); null when there is no such bucket. Not tracked.</summary>
    ValueTask<PortfolioBucket?> FindByNameAsync(string name, CancellationToken cancellationToken);

    /// <summary>True when a bucket other than <paramref name="exceptBucketId"/> has this name (ignoring case), archived ones included.</summary>
    ValueTask<bool> NameTakenAsync(string name, Guid? exceptBucketId, CancellationToken cancellationToken);

    /// <summary>The names of all buckets (archived ones included) that start with <paramref name="prefix"/> (ignoring case), except bucket <paramref name="exceptBucketId"/>.</summary>
    ValueTask<IReadOnlyList<string>> NamesStartingWithAsync(string prefix, Guid? exceptBucketId, CancellationToken cancellationToken);

    /// <summary>The default bucket of an account, tracked so that it can be renamed or archived; null when it has none yet.</summary>
    ValueTask<PortfolioBucket?> FindDefaultForAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The names of the default buckets that exist for these accounts (an account without one is left out). For reading only.</summary>
    ValueTask<IReadOnlyDictionary<Guid, string>> DefaultNamesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>The names of these buckets (a bucket that does not exist is left out). For reading only.</summary>
    ValueTask<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> bucketIds, CancellationToken cancellationToken);

    void Add(PortfolioBucket bucket);
}
