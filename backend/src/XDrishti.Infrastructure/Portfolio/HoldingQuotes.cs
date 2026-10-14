using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The portfolio module's side of the quote poll (ADR 0019): which securities the open holdings hold, and the one-statement write of the quotes into them. The statement mirrors the rules of <c>Holding.ApplyQuote</c>
/// (price, previous close kept when the quote has none - and the time it was taken, <c>previous_close_at</c>, set to the fetch time whenever the quote carries one - price time, source quote; open holdings only; a live price newer than the fetch time keeps its price, time and source - only the previous close is taken, ADR 0024) for all quoted instruments at once, whatever the number of accounts and holdings, and reads the same
/// accounts as <see cref="ListTargetsAsync"/> (active, Portfolio role on).
/// </summary>
internal sealed class HoldingQuotes(XDrishtiDbContext db, IPortfolioHistoryCache histories) : IHoldingQuotes
{
    private const string DhanBroker = "dhan";

    private const string NseEquitySegment = "NSE_EQ";

    public async ValueTask<QuoteTargets> ListTargetsAsync(CancellationToken cancellationToken)
    {
        // the accounts the Portfolio screen reads: active, with the Portfolio role
        var open = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.BrokerAccounts.AsNoTracking() on holding.AccountId equals account.Id
            where account.ArchivedAt == null && account.Portfolio.Enabled && holding.Status == HoldingStatus.Open
            select new { holding.InstrumentId, holding.Exchange, holding.SecurityId })
            .ToListAsync(cancellationToken);
        var securities = open.Select(h => h.InstrumentId is { } id ? "i:" + id : "s:" + h.Exchange.Trim().ToUpperInvariant() + ":" + h.SecurityId.Trim()).Distinct(StringComparer.Ordinal).Count();
        var instrumentIds = open.Where(h => h.InstrumentId is not null).Select(h => h.InstrumentId!.Value).Distinct().ToArray();
        if (instrumentIds.Length == 0)
        {
            return new QuoteTargets(securities, []);
        }

        var nse = await (
            from broker in db.InstrumentBrokerIds.AsNoTracking()
            join instrument in db.Instruments.AsNoTracking() on broker.InstrumentId equals instrument.Id
            where broker.Broker == DhanBroker && broker.Segment == NseEquitySegment && instrumentIds.Contains(broker.InstrumentId) && instrument.RetiredAt == null
            orderby broker.BrokerId
            select new { broker.InstrumentId, broker.BrokerId })
            .ToListAsync(cancellationToken);
        return new QuoteTargets(securities, [.. nse.Select(n => new QuoteTarget(n.InstrumentId, n.BrokerId))]);
    }

    public async ValueTask<int> ApplyAsync(IReadOnlyList<HoldingQuote> quotes, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        // Ordered by instrument id: the same batch always walks the rows in the same order (the lock order below does not depend on how the caller built the list).
        var usable = quotes.Where(q => q.LastPrice > 0m).DistinctBy(q => q.InstrumentId).OrderBy(q => q.InstrumentId).ToList();
        if (usable.Count == 0)
        {
            return 0;
        }

        var instruments = usable.Select(q => q.InstrumentId).ToArray();
        var lasts = usable.Select(q => Round(q.LastPrice)).ToArray();
        var previous = usable.Select(q => q.PreviousClose is > 0m ? Round(q.PreviousClose.Value) : (decimal?)null).ToArray();
        var time = at.ToUniversalTime();

        // One statement for the whole batch: every open holding of a quoted instrument in every account that ListTargetsAsync reads (active account with the Portfolio role). The previous close is kept
        // when the quote has none. The rows are locked first, in holding id order, with SKIP LOCKED: the statement never waits for a row, so it cannot deadlock with a holdings sync that holds several
        // of them (the sync's updates follow the order of .NET's Guid comparison, PostgreSQL's uuid order differs, so no common lock order could be promised). A row a sync holds is left for the next
        // tick; the sync writes a price as fresh as the quote, and a sync that read a row before this statement changed it is refused by the row version and reads again (PortfolioUnitOfWork).
        var updated = await db.Database.ExecuteSqlAsync(
            $"""
            WITH target AS (
                SELECT h.id, q.last_price, q.previous_close
                FROM portfolio.holdings AS h
                JOIN unnest({instruments}::uuid[], {lasts}::numeric[], {previous}::numeric[]) AS q(instrument_id, last_price, previous_close) ON q.instrument_id = h.instrument_id
                WHERE h.status = 'open'
                  AND EXISTS (
                      SELECT 1 FROM accounts.broker_accounts AS a
                      JOIN accounts.portfolio_settings AS p ON p.account_id = a.id
                      WHERE a.id = h.account_id AND a.archived_at IS NULL AND p.enabled)
                ORDER BY h.id
                FOR UPDATE OF h SKIP LOCKED
            )
            UPDATE portfolio.holdings AS h
            SET last_price = CASE WHEN h.last_price_source = 'live' AND h.last_price_at > {time} THEN h.last_price ELSE t.last_price END,
                previous_close = COALESCE(t.previous_close, h.previous_close),
                previous_close_at = CASE WHEN t.previous_close IS NOT NULL THEN {time} ELSE h.previous_close_at END,
                last_price_at = CASE WHEN h.last_price_source = 'live' AND h.last_price_at > {time} THEN h.last_price_at ELSE {time} END,
                last_price_source = CASE WHEN h.last_price_source = 'live' AND h.last_price_at > {time} THEN h.last_price_source ELSE 'quote' END
            FROM target AS t
            WHERE h.id = t.id
            """,
            cancellationToken);
        if (updated > 0)
        {
            histories.Invalidate(); // today's point of the cached histories used the older prices
        }

        return updated;
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
