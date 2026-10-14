using XDrishti.Domain.Market;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// Which price wins when a quote, a live tick and a sync meet in a holding (ADR 0019 §3, ADR 0024). Pure rules over the price held (its source and time); the batch statements of the stores in Infrastructure
/// (<c>HoldingQuotes</c>, <c>LivePrices</c>) apply the same rules in SQL. A price is as old as its <c>last_price_at</c>: the tick time of a live price, the fetch time of a quote, the sync time of a synced price.
/// </summary>
public static class LivePricePolicy
{
    /// <summary>The first minute of the continuous session, India time. A tick stamped before it comes from the pre-open and is only indicative.</summary>
    public static readonly TimeOnly MarketOpen = new(9, 15);

    /// <summary>True for a tick stamped before the open (09:15 India time): its price is recorded but never replaces a quote of the same India day.</summary>
    public static bool IsIndicative(DateTimeOffset tickAt) => TimeOnly.FromDateTime(IndiaTime.ToIst(tickAt).DateTime) < MarketOpen;

    /// <summary>
    /// Whether a live tick replaces the price held: yes when nothing is held with a time or the tick is strictly newer than it (so a live price never overwrites a newer quote, nor an older live price), or when it has the very same time
    /// and a different price (Dhan stamps trades with whole epoch seconds, so two trades of one second are two ticks; an equal time with an equal price is the same tick again), except that an indicative tick does not replace a quote of
    /// the same India day (as the tick's).
    /// </summary>
    public static bool LiveReplaces(PriceSource heldSource, DateTimeOffset? heldAt, DateTimeOffset tickAt, decimal heldPrice, decimal tickPrice)
    {
        if (heldAt is not { } held)
        {
            return true;
        }

        if (tickAt < held || (tickAt == held && tickPrice == heldPrice))
        {
            return false;
        }

        return !(IsIndicative(tickAt) && heldSource == PriceSource.Quote && IndiaTime.Today(held) == IndiaTime.Today(tickAt));
    }

    /// <summary>Whether a quote fetched at <paramref name="fetchedAt"/> replaces the price held: not when a live price newer than the fetch is held (the quote's own price is older than that tick).</summary>
    public static bool QuoteReplaces(PriceSource heldSource, DateTimeOffset? heldAt, DateTimeOffset fetchedAt) =>
        !(heldSource == PriceSource.Live && heldAt is { } held && held > fetchedAt);
}
