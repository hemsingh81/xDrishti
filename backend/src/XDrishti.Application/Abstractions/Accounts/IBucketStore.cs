using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Persistence port for portfolio buckets. New buckets are saved by <see cref="IAccountUnitOfWork"/>.</summary>
public interface IBucketStore
{
    ValueTask<IReadOnlyList<PortfolioBucket>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The stored spelling of a bucket name (looked up ignoring case); null when there is no such bucket.</summary>
    ValueTask<string?> FindNameAsync(string name, CancellationToken cancellationToken);

    void Add(PortfolioBucket bucket);
}
