using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>
/// The stored daily context of baskets (spec BK-08): <c>market.basket_context_daily</c>, one row per basket and day, written once and never updated (the runtime role has insert and select only). Nothing here places an order.
/// </summary>
public interface IBasketContextStore
{
    /// <summary>Inserts the row unless the basket already has one for the day; true when it was inserted.</summary>
    ValueTask<bool> InsertOnceAsync(BasketContextRow row, CancellationToken cancellationToken);

    /// <summary>The newest row of the basket dated before <paramref name="beforeExclusive"/>; null when none.</summary>
    ValueTask<BasketContextRow?> LatestBeforeAsync(Guid basketId, DateOnly beforeExclusive, CancellationToken cancellationToken);

    /// <summary>The days of the basket that have a row, from <paramref name="from"/> to <paramref name="to"/> (both included).</summary>
    ValueTask<IReadOnlySet<DateOnly>> DatesAsync(Guid basketId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The newest rows of the basket in the range (both bounds optional), newest first, at most <paramref name="take"/>.</summary>
    ValueTask<IReadOnlyList<BasketContextRow>> ListAsync(Guid basketId, DateOnly? from, DateOnly? to, int take, CancellationToken cancellationToken);
}
