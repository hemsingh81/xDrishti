using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Live.Push;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The hub's read of prices (ADR 0024 §7): for each instrument asked for, the newest price time among the open holdings' price columns (<c>last_price</c>, <c>last_price_at</c>, <c>last_price_source</c>: where the live feed, the quote poll and the
/// holdings sync all write) and <c>market.live_quotes</c> (instruments that are not holdings, source <c>live</c>). A price without a time or not above zero is left out. Two indexed reads for the whole list, no tracking.
/// </summary>
internal sealed class LivePriceReader(XDrishtiDbContext db) : ILivePriceReader
{
    public async ValueTask<IReadOnlyList<InstrumentPrice>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        if (instrumentIds.Count == 0)
        {
            return [];
        }

        var ids = instrumentIds.ToArray();
        var held = await db.Holdings.AsNoTracking()
            .Where(h => h.Status == HoldingStatus.Open && h.InstrumentId != null && ids.Contains(h.InstrumentId.Value) && h.LastPriceAt != null && h.LastPrice > 0m)
            .Select(h => new { InstrumentId = h.InstrumentId!.Value, h.LastPrice, At = h.LastPriceAt!.Value, h.LastPriceSource })
            .ToListAsync(cancellationToken);
        var quotes = await db.LiveQuotes.AsNoTracking()
            .Where(q => ids.Contains(q.InstrumentId) && q.LastPrice > 0m)
            .Select(q => new { q.InstrumentId, q.LastPrice, At = q.TickAt })
            .ToListAsync(cancellationToken);

        var newest = new Dictionary<Guid, InstrumentPrice>();
        foreach (var h in held)
        {
            Offer(newest, new InstrumentPrice(h.InstrumentId, h.LastPrice, h.At.ToUniversalTime(), h.LastPriceSource.ToString().ToLowerInvariant()));
        }

        foreach (var q in quotes)
        {
            Offer(newest, new InstrumentPrice(q.InstrumentId, q.LastPrice, q.At.ToUniversalTime(), HoldingData.LiveSource));
        }

        return [.. newest.Values.OrderBy(p => p.InstrumentId)];
    }

    /// <summary>The newer price wins; at the same time the higher-ranked source (live, then quote, then sync) so the answer does not flip between reads.</summary>
    private static void Offer(Dictionary<Guid, InstrumentPrice> newest, InstrumentPrice price)
    {
        if (!newest.TryGetValue(price.InstrumentId, out var have) || price.PriceAt > have.PriceAt || (price.PriceAt == have.PriceAt && Rank(price.Source) > Rank(have.Source)))
        {
            newest[price.InstrumentId] = price;
        }
    }

    private static int Rank(string source) => source switch { HoldingData.LiveSource => 2, HoldingData.QuoteSource => 1, _ => 0 };
}
