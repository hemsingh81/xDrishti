using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>
/// Persistence port for baskets and their members, for use cases that change them. Everything returned is tracked by the scoped context, and new and
/// changed rows are saved by <see cref="IMarketUnitOfWork"/> together with the tracking rows and audit events collected in the same request.
/// </summary>
public interface IBasketStore
{
    /// <summary>The basket with this id, archived or not; null when there is none.</summary>
    ValueTask<Basket?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The baskets that are not archived, in their order (position, then creation, then id).</summary>
    ValueTask<IReadOnlyList<Basket>> ListActiveAsync(CancellationToken cancellationToken);

    void Add(Basket basket);

    /// <summary>
    /// Whether an active basket (other than <paramref name="exceptId"/>) has this name, compared the way the database's unique index does. For reading only;
    /// asked again after a refused save, because the index may count two names as equal that the in-memory comparison of the handlers does not.
    /// </summary>
    ValueTask<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>The instruments that are members of the basket now, in their order (position, then start, then id).</summary>
    ValueTask<IReadOnlyList<BasketMember>> ListActiveMembersAsync(Guid basketId, CancellationToken cancellationToken);

    void Add(BasketMember member);
}
