using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The price alerts (ADR 0022). Both ports are served here: the quote poll's (<see cref="IPriceAlerts"/>) and the owner's (<see cref="IPriceAlertStore"/>). Reads are projections without tracking, joined with the instrument master for the symbol and name;
/// a new alert is collected and saved with its audit event. Triggering and cancelling are conditional updates (<c>WHERE status = 'active'</c>) that execute at once inside the current transaction, so an alert changes status at most once. Nothing is deleted.
/// </summary>
internal sealed class PriceAlertStore(XDrishtiDbContext db) : IPriceAlerts, IPriceAlertStore
{
    private const string DhanBroker = "dhan";

    private const string NseEquitySegment = "NSE_EQ";

    private const string DailyTimeframe = "1D";

    private const string ExchangePrefix = "NSE:";

    public async ValueTask<IReadOnlyList<ActiveAlert>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from alert in db.PriceAlerts.AsNoTracking()
            where alert.Status == AlertStatus.Active
            join instrument in db.Instruments.AsNoTracking() on alert.InstrumentId equals instrument.Id
            orderby alert.CreatedAt, alert.Id
            select new
            {
                alert.Id,
                alert.InstrumentId,
                instrument.CanonicalSymbol,
                alert.Direction,
                alert.Level,
                Retired = instrument.RetiredAt != null,
                SecurityId = db.InstrumentBrokerIds
                    .Where(b => b.InstrumentId == alert.InstrumentId && b.Broker == DhanBroker && b.Segment == NseEquitySegment)
                    .OrderBy(b => b.BrokerId)
                    .Select(b => b.BrokerId)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new ActiveAlert(r.Id, r.InstrumentId, Ticker(r.CanonicalSymbol), r.Retired ? null : r.SecurityId, r.Direction, r.Level))];
    }

    public async ValueTask<bool> TryTriggerAsync(Guid alertId, decimal price, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var rounded = Math.Round(price, PriceAlert.LevelDecimals, MidpointRounding.AwayFromZero);
        var time = at.ToUniversalTime();
        var changed = await db.PriceAlerts
            .Where(a => a.Id == alertId && a.Status == AlertStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AlertStatus.Triggered)
                .SetProperty(a => a.TriggeredAt, time)
                .SetProperty(a => a.TriggerPrice, rounded), cancellationToken);
        return changed == 1;
    }

    public async ValueTask<bool> TryCancelAsync(Guid alertId, CancellationToken cancellationToken)
    {
        var changed = await db.PriceAlerts
            .Where(a => a.Id == alertId && a.Status == AlertStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AlertStatus.Cancelled), cancellationToken);
        return changed == 1;
    }

    public async ValueTask<AlertInstrument?> FindInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var row = await db.Instruments.AsNoTracking()
            .Where(i => i.Id == instrumentId)
            .Select(i => new
            {
                i.Id,
                i.CanonicalSymbol,
                i.DisplayName,
                i.Type,
                i.Exchange,
                Retired = i.RetiredAt != null,
                Quotable = db.InstrumentBrokerIds.Any(b => b.InstrumentId == i.Id && b.Broker == DhanBroker && b.Segment == NseEquitySegment),
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new AlertInstrument(row.Id, Ticker(row.CanonicalSymbol), row.DisplayName, row.Type, row.Exchange, row.Retired, row.Quotable);
    }

    public async ValueTask<KnownPrice?> GetKnownPriceAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        // The accounts the Portfolio screen reads: active, with the Portfolio role. The newest price among the open holdings of the instrument wins.
        var held = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.BrokerAccounts.AsNoTracking() on holding.AccountId equals account.Id
            where holding.InstrumentId == instrumentId && holding.Status == HoldingStatus.Open && holding.LastPrice > 0m && account.ArchivedAt == null && account.Portfolio.Enabled
            orderby holding.LastPriceAt descending, holding.Id
            select (decimal?)holding.LastPrice)
            .FirstOrDefaultAsync(cancellationToken);
        if (held is { } price)
        {
            return new KnownPrice(price, "holding");
        }

        var close = await db.BarsAgg.AsNoTracking()
            .Where(b => b.InstrumentId == instrumentId && b.Timeframe == DailyTimeframe)
            .OrderByDescending(b => b.Ts)
            .Select(b => new { b.Close, b.Ts })
            .FirstOrDefaultAsync(cancellationToken);
        return close is { Close: > 0.0 } ? new KnownPrice(Math.Round((decimal)close.Close, PriceAlert.LevelDecimals, MidpointRounding.AwayFromZero), "close", IndiaTime.Today(close.Ts)) : null;
    }

    public async ValueTask<int> CountActiveAsync(CancellationToken cancellationToken) =>
        await db.PriceAlerts.AsNoTracking().CountAsync(a => a.Status == AlertStatus.Active, cancellationToken);

    public void Add(PriceAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        db.PriceAlerts.Add(alert);
    }

    public async ValueTask<AlertRow?> FindAsync(Guid alertId, CancellationToken cancellationToken)
    {
        var found = await ReadAsync(Joined().Where(x => x.Alert.Id == alertId), cancellationToken);
        return found.Count == 0 ? null : found[0];
    }

    public async ValueTask<(IReadOnlyList<AlertRow> Items, int Total)> ListAsync(AlertStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Joined();
        if (status is { } wanted)
        {
            query = query.Where(x => x.Alert.Status == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await ReadAsync(query.OrderByDescending(x => x.Alert.CreatedAt).ThenByDescending(x => x.Alert.Id).Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return (rows, total);
    }

    public async ValueTask<IReadOnlyList<AlertRow>> ListTriggeredSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var from = since.ToUniversalTime();
        return await ReadAsync(
            Joined().Where(x => x.Alert.Status == AlertStatus.Triggered && x.Alert.TriggeredAt >= from).OrderByDescending(x => x.Alert.TriggeredAt).ThenByDescending(x => x.Alert.Id),
            cancellationToken);
    }

    private IQueryable<JoinedAlert> Joined() =>
        from alert in db.PriceAlerts.AsNoTracking()
        join instrument in db.Instruments.AsNoTracking() on alert.InstrumentId equals instrument.Id
        select new JoinedAlert { Alert = alert, CanonicalSymbol = instrument.CanonicalSymbol, DisplayName = instrument.DisplayName };

    private static async ValueTask<IReadOnlyList<AlertRow>> ReadAsync(IQueryable<JoinedAlert> query, CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(r => new AlertRow(
                r.Alert.Id,
                r.Alert.InstrumentId,
                Ticker(r.CanonicalSymbol),
                r.DisplayName,
                r.Alert.Direction,
                r.Alert.Level,
                r.Alert.Note,
                r.Alert.Status,
                r.Alert.CreatedAt,
                r.Alert.TriggeredAt,
                r.Alert.TriggerPrice)),
        ];
    }

    /// <summary>The ticker of a canonical symbol: <c>NSE:INFY</c> → <c>INFY</c>.</summary>
    private static string Ticker(string canonicalSymbol) =>
        canonicalSymbol.StartsWith(ExchangePrefix, StringComparison.Ordinal) ? canonicalSymbol[ExchangePrefix.Length..] : canonicalSymbol;

    /// <summary>A class with initialisers, not a record with a constructor: filters on it are translated into the query.</summary>
    private sealed class JoinedAlert
    {
        public required PriceAlert Alert { get; init; }

        public required string CanonicalSymbol { get; init; }

        public required string DisplayName { get; init; }
    }
}
