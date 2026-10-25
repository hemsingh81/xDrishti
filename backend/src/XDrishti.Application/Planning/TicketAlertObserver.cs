using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Watches the live prices of Armed tickets and stores a crossing of the stored stop or target level once (spec P7-20, ADR 0115). It runs inside the flush of <c>xd-feed</c> through <see cref="ILiveTickObserver"/> and is registered only when
/// <c>Planning:TicketAlerts:Enabled</c> is true. The list of watched tickets is cached (<c>WatchedCacheSeconds</c>); each flush costs one lookup in memory and one insert per new crossing. It swallows and logs its own failure, so a database fault
/// never costs a price. It changes no ticket, reads no credential and cannot place an order.
/// </summary>
internal sealed class TicketAlertObserver(IServiceScopeFactory scopes, ITicketAlertSettings options, TimeProvider clock, ILogger<TicketAlertObserver> logger) : ILiveTickObserver
{
    private readonly HashSet<(long Item, string Kind)> _stored = [];
    private Dictionary<Guid, List<WatchedTicket>> _watched = [];
    private DateOnly _watchedFor;
    private long _loadedAt = -1;

    public async ValueTask ObserveAsync(IReadOnlyList<LivePrice> firmTicks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firmTicks);
        try
        {
            var inSession = firmTicks.Where(t => TicketCrossing.InSession(t.TickAt)).ToList();
            if (inSession.Count == 0)
            {
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ITicketAlertStore>();
            await RefreshAsync(scope.ServiceProvider, store, cancellationToken);
            var stored = 0;
            foreach (var tick in inSession)
            {
                if (!_watched.TryGetValue(tick.InstrumentId, out var tickets) || IndiaTime.Today(tick.TickAt) != _watchedFor)
                {
                    continue;
                }

                foreach (var ticket in tickets)
                {
                    foreach (var kind in TicketCrossing.Crossed(ticket.Side, ticket.Stop, ticket.Target, tick.Price))
                    {
                        if (_stored.Contains((ticket.ItemId, kind)))
                        {
                            continue;
                        }

                        var level = kind == CrossingKinds.Stop ? ticket.Stop!.Value : ticket.Target!.Value;
                        var added = await store.AddCrossingAsync(new NewCrossing(ticket.PlanId, ticket.ItemId, ticket.Session, kind, level, kind == CrossingKinds.Stop ? ticket.StopAnchor : null, TicketCrossing.Round(tick.Price), tick.TickAt), cancellationToken);
                        _stored.Add((ticket.ItemId, kind));
                        if (added)
                        {
                            stored++;
                            TicketAlertLog.Crossed(logger, ticket.ItemId, kind);
                        }
                    }
                }
            }

            if (stored > 0)
            {
                TicketAlertLog.Stored(logger, stored);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed alert check must never cost a price: the flush carries on, the crossing is tried again by the next tick
        catch (Exception failure)
#pragma warning restore CA1031
        {
            TicketAlertLog.ObserveFailed(logger, failure.GetType().Name);
        }
    }

    private async ValueTask RefreshAsync(IServiceProvider services, ITicketAlertStore store, CancellationToken cancellationToken)
    {
        var now = clock.GetTimestamp();
        var today = IndiaTime.Today(clock.GetUtcNow());
        if (_loadedAt >= 0 && _watchedFor == today && clock.GetElapsedTime(_loadedAt, now) < TimeSpan.FromSeconds(options.WatchedCacheSeconds))
        {
            return;
        }

        _loadedAt = now; // stamped before the read: a failing database is asked again after the cache time, not on every flush
        var tickets = await store.ListWatchedAsync(today, cancellationToken);
        var found = tickets.Count == 0
            ? new Dictionary<string, InstrumentSecurity>()
            : (await services.GetRequiredService<IInstrumentSecurities>().FindAsync([.. tickets.Select(t => t.Symbol).Distinct(StringComparer.Ordinal)], cancellationToken)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var map = new Dictionary<Guid, List<WatchedTicket>>();
        foreach (var ticket in tickets)
        {
            if (found.TryGetValue(ticket.Symbol, out var security))
            {
                if (!map.TryGetValue(security.InstrumentId, out var list))
                {
                    map[security.InstrumentId] = list = [];
                }

                list.Add(ticket);
            }
        }

        if (_watchedFor != today)
        {
            _stored.Clear();
        }

        _watched = map;
        _watchedFor = today;
    }
}
