using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Live;

/// <summary>
/// <c>market.live_quotes</c> (ADR 0024 §3): one upsert statement for a whole batch, newest tick wins (the same second with another price counts: Dhan stamps whole seconds). The table is emptied at the start of each window by <c>market.clear_live_quotes()</c>, a database function that runs with its owner's rights
/// (the runtime role has no DELETE or TRUNCATE).
/// </summary>
internal sealed class LiveQuotes(XDrishtiDbContext db) : ILiveQuotes
{
    public async ValueTask<int> UpsertAsync(IReadOnlyList<LivePrice> prices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prices);
        var usable = prices.Where(p => p.Price > 0m).GroupBy(p => p.InstrumentId).Select(g => g.MaxBy(p => p.TickAt)!).OrderBy(p => p.InstrumentId).ToList();
        if (usable.Count == 0)
        {
            return 0;
        }

        var instruments = usable.Select(p => p.InstrumentId).ToArray();
        var lasts = usable.Select(p => Math.Round(p.Price, 4, MidpointRounding.AwayFromZero)).ToArray();
        var ticks = usable.Select(p => p.TickAt.ToUniversalTime()).ToArray();

        // A tick older than the stored one, or the same tick again, changes nothing (the WHERE of the conflict branch), so a late batch cannot move a price back.
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO market.live_quotes AS lq (instrument_id, last_price, tick_at)
            SELECT q.instrument_id, q.last_price, q.tick_at
            FROM unnest({instruments}::uuid[], {lasts}::numeric[], {ticks}::timestamptz[]) AS q(instrument_id, last_price, tick_at)
            ORDER BY q.instrument_id
            ON CONFLICT (instrument_id) DO UPDATE
            SET last_price = EXCLUDED.last_price, tick_at = EXCLUDED.tick_at
            WHERE lq.tick_at < EXCLUDED.tick_at OR (lq.tick_at = EXCLUDED.tick_at AND lq.last_price <> EXCLUDED.last_price)
            """,
            cancellationToken);
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync("SELECT market.clear_live_quotes()", cancellationToken);
}
