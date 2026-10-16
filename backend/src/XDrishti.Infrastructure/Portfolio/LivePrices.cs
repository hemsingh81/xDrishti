using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The portfolio module's side of the live feed (ADR 0024 §3): which instruments the open holdings hold, and the one-statement write of the live prices into them. The statement mirrors <c>Holding.ApplyLive</c> and
/// <see cref="LivePricePolicy"/> for all instruments at once, whatever the number of accounts and holdings, and reads the same accounts as <see cref="HoldingQuotes"/> (active, Portfolio role on):
/// <list type="bullet">
/// <item>open holdings only; a price is replaced only by a strictly newer tick, or by a tick of the same second with another price (Dhan stamps whole seconds); a quote fetched after the tick, a live price as new or newer, a sync after it all stay;</item>
/// <item>an indicative tick (before 09:15 India time) does not replace a quote of the same India day (India time is UTC + 5:30 all year, so no time-zone database is needed); both this and the newer-price rule are checked again on the locked row, so a quote committed between the statement's snapshot and the lock is not replaced by a stale or indicative tick;</item>
/// <item><c>previous_close</c> is never written; price, price time (the tick time) and source <c>live</c> are.</item>
/// </list>
/// The rows are locked first, in holding id order, with SKIP LOCKED: the statement never waits and cannot deadlock with a holdings sync. The statement reports how many rows qualified and how many it wrote; a row left out because a sync held it
/// is tried once more, then dropped for this flush and logged (the next tick of the instrument writes it). The row version (xmin) changes with the write, so a sync that read the row before is refused and reads again (PortfolioUnitOfWork).
/// The portfolio history cache lives in <c>xd-api</c> (an in-process singleton: this code runs in <c>xd-feed</c> and cannot clear it), so the 'today' point of a cached value history can be as old as that cache's lifetime (60 seconds).
/// </summary>
internal sealed class LivePrices(XDrishtiDbContext db, ILogger<LivePrices> logger) : ILivePrices
{
    private const string DhanBroker = "dhan";

    private const string NseEquitySegment = "NSE_EQ";

    private const string WriteSql =
        """
        WITH cand AS (
            SELECT h.id, q.last_price, q.tick_at
            FROM portfolio.holdings AS h
            JOIN unnest(@instruments, @prices, @ticks) AS q(instrument_id, last_price, tick_at) ON q.instrument_id = h.instrument_id
            WHERE h.status = 'open'
              AND (h.last_price_at IS NULL OR h.last_price_at < q.tick_at OR (h.last_price_at = q.tick_at AND h.last_price <> q.last_price))
              AND NOT (
                  h.last_price_source = 'quote'
                  AND h.last_price_at IS NOT NULL
                  AND ((q.tick_at AT TIME ZONE 'UTC') + interval '330 minutes')::time < time '09:15'
                  AND ((h.last_price_at AT TIME ZONE 'UTC') + interval '330 minutes')::date = ((q.tick_at AT TIME ZONE 'UTC') + interval '330 minutes')::date)
              AND EXISTS (
                  SELECT 1 FROM accounts.broker_accounts AS a
                  JOIN accounts.portfolio_settings AS p ON p.account_id = a.id
                  WHERE a.id = h.account_id AND a.archived_at IS NULL AND p.enabled)
        ),
        target AS (
            SELECT c.id, c.last_price, c.tick_at
            FROM cand AS c
            JOIN portfolio.holdings AS h ON h.id = c.id
            WHERE h.status = 'open'
              AND (h.last_price_at IS NULL OR h.last_price_at < c.tick_at OR (h.last_price_at = c.tick_at AND h.last_price <> c.last_price))
              AND NOT (
                  h.last_price_source = 'quote'
                  AND h.last_price_at IS NOT NULL
                  AND ((c.tick_at AT TIME ZONE 'UTC') + interval '330 minutes')::time < time '09:15'
                  AND ((h.last_price_at AT TIME ZONE 'UTC') + interval '330 minutes')::date = ((c.tick_at AT TIME ZONE 'UTC') + interval '330 minutes')::date)
            ORDER BY c.id
            FOR UPDATE OF h SKIP LOCKED
        ),
        written AS (
            UPDATE portfolio.holdings AS h
            SET last_price = t.last_price,
                last_price_at = t.tick_at,
                last_price_source = 'live'
            FROM target AS t
            WHERE h.id = t.id
            RETURNING h.id
        )
        SELECT (SELECT count(*) FROM cand)::int, (SELECT count(*) FROM written)::int
        """;

    public async ValueTask<IReadOnlySet<Guid>> ListHeldInstrumentsAsync(CancellationToken cancellationToken)
    {
        var ids = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.BrokerAccounts.AsNoTracking() on holding.AccountId equals account.Id
            where account.ArchivedAt == null && account.Portfolio.Enabled && holding.Status == HoldingStatus.Open && holding.InstrumentId != null
            select holding.InstrumentId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async ValueTask<IReadOnlyList<HeldSecurity>> ListHeldSecuritiesAsync(CancellationToken cancellationToken)
    {
        // the accounts the Portfolio screen reads, as in HoldingQuotes.ListTargetsAsync
        var open = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.BrokerAccounts.AsNoTracking() on holding.AccountId equals account.Id
            where account.ArchivedAt == null && account.Portfolio.Enabled && holding.Status == HoldingStatus.Open && holding.InstrumentId != null
            select new { InstrumentId = holding.InstrumentId!.Value, holding.Qty, holding.LastPrice })
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        var values = open.GroupBy(h => h.InstrumentId).ToDictionary(g => g.Key, g => g.Sum(h => h.Qty * h.LastPrice));
        var ids = values.Keys.ToArray();
        var nse = await (
            from broker in db.InstrumentBrokerIds.AsNoTracking()
            join instrument in db.Instruments.AsNoTracking() on broker.InstrumentId equals instrument.Id
            where broker.Broker == DhanBroker && broker.Segment == NseEquitySegment && ids.Contains(broker.InstrumentId) && instrument.RetiredAt == null
            select new { broker.InstrumentId, broker.BrokerId })
            .ToListAsync(cancellationToken);
        return [.. nse
            .GroupBy(n => n.InstrumentId)
            .Select(g => new HeldSecurity(g.Key, g.Min(n => n.BrokerId)!, values[g.Key]))
            .OrderByDescending(h => h.Value)
            .ThenBy(h => h.SecurityId, StringComparer.Ordinal)];
    }

    public async ValueTask<int> ApplyAsync(IReadOnlyList<LivePrice> prices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prices);

        // Ordered by instrument id: the same batch always walks the rows in the same order.
        var usable = prices.Where(p => p.Price > 0m).GroupBy(p => p.InstrumentId).Select(g => g.MaxBy(p => p.TickAt)!).OrderBy(p => p.InstrumentId).ToList();
        if (usable.Count == 0)
        {
            return 0;
        }

        var instruments = usable.Select(p => p.InstrumentId).ToArray();
        var lasts = usable.Select(p => Math.Round(p.Price, 4, MidpointRounding.AwayFromZero)).ToArray();
        var ticks = usable.Select(p => p.TickAt.ToUniversalTime()).ToArray();

        var total = 0;
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var (qualified, written) = await WriteAsync(instruments, lasts, ticks, cancellationToken);
                total += written;
                var left = qualified - written;
                if (left <= 0)
                {
                    break;
                }

                if (attempt == 2)
                {
                    LivePricesLog.Dropped(logger, left);
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return total;
    }

    private async ValueTask<(int Qualified, int Written)> WriteAsync(Guid[] instruments, decimal[] lasts, DateTimeOffset[] ticks, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = WriteSql;
        command.Parameters.Add(new NpgsqlParameter("instruments", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = instruments });
        command.Parameters.Add(new NpgsqlParameter("prices", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = lasts });
        command.Parameters.Add(new NpgsqlParameter("ticks", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = ticks });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }
}

/// <summary>Structured log events of the live price write (event id 4091). Counts only.</summary>
internal static partial class LivePricesLog
{
    [LoggerMessage(EventId = 4091, Level = LogLevel.Warning, Message = "feed.price_dropped rows={Rows}: held by a holdings sync, left for the next tick")]
    public static partial void Dropped(ILogger logger, int rows);
}
