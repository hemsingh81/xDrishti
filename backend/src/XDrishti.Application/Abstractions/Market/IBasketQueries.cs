using XDrishti.Application.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>Read side of the baskets (projected, not tracked).</summary>
public interface IBasketQueries
{
    /// <summary>The baskets in their order, with the number of members now; archived ones only when asked for.</summary>
    ValueTask<IReadOnlyList<BasketView>> ListAsync(bool includeArchived, CancellationToken cancellationToken);

    ValueTask<BasketView?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The members of a basket (archived or not) in their order; null when there is no such basket. Without <paramref name="cutoff"/> the members now,
    /// otherwise those that were members when the cutoff came (see <see cref="XDrishti.Domain.Market.Baskets.BasketMember.WasMemberAt"/>).
    /// </summary>
    ValueTask<BasketMembers?> MembersAsync(Guid basketId, DateTimeOffset? cutoff, DateOnly? asOf, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="take"/> additions and removals of a basket; null when there is no such basket.</summary>
    ValueTask<BasketHistory?> HistoryAsync(Guid basketId, int take, CancellationToken cancellationToken);

    /// <summary>The baskets (not archived) that hold the instrument now; null when there is no such instrument.</summary>
    ValueTask<IReadOnlyList<InstrumentBasketView>?> ForInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken);
}
