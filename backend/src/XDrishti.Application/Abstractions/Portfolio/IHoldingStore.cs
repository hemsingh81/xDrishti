using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Write side of holdings: tracked holdings with their lots. Saved by <see cref="IPortfolioUnitOfWork"/>.</summary>
public interface IHoldingStore
{
    /// <summary>True when the account has any holding, open or closed.</summary>
    ValueTask<bool> AnyForAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Every holding of the account (open and closed) with its lots, tracked.</summary>
    ValueTask<IReadOnlyList<Holding>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>One holding with its lots, tracked; null when it does not exist.</summary>
    ValueTask<Holding?> FindAsync(Guid holdingId, CancellationToken cancellationToken);

    void Add(Holding holding);
}
