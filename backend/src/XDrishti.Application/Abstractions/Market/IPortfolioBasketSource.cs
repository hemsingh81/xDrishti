using XDrishti.Application.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>
/// The portfolio buckets seen as read-only system baskets (ADR 0014): a view over the buckets and their current holdings, nothing is stored as a basket.
/// A basket id of this port is a bucket id. Listed buckets are those a user can pick (<c>GET /api/portfolio-buckets</c>): not archived, custom or the default
/// bucket of an active account that has the Portfolio role. Holdings count when their quantity is above zero, their account is active with the Portfolio role
/// and they are mapped to an instrument of the master list (the others are left out).
/// </summary>
public interface IPortfolioBasketSource
{
    /// <summary>The system baskets (kind portfolio) in their order: account name, then name; the member counts come from one grouped query.</summary>
    ValueTask<IReadOnlyList<BasketView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Whether the id is a listed portfolio bucket.</summary>
    ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The instruments of the bucket's current holdings, in their order (symbol); null when the id is not a listed bucket.</summary>
    ValueTask<BasketMembers?> MembersAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The bucket's name and the ids of its instruments in member order; null when the id is not a listed bucket.</summary>
    ValueTask<PortfolioBasketInstruments?> InstrumentsAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>The instruments held in a portfolio bucket, in member order.</summary>
public sealed record PortfolioBasketInstruments(Guid Id, string Name, IReadOnlyList<Guid> InstrumentIds);
