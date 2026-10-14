using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Live;

/// <summary>
/// The price sink of the live feed (ADR 0024, P2-10 criteria 9 to 14). <see cref="Add"/> puts a tick into the <see cref="TickCoalescer"/>; <see cref="FlushAsync"/> (every <c>Live:FlushSeconds</c>) takes what changed since the last flush and
/// <list type="number">
/// <item>splits it into instruments the open holdings hold and the others (indices, alert-only instruments);</item>
/// <item>writes the held ones into the holdings in one statement (<see cref="ILivePrices"/>) and the others into <c>market.live_quotes</c> (<see cref="ILiveQuotes"/>);</item>
/// <item>evaluates the active price alerts of the flushed instruments with the <see cref="PriceAlertChecker"/> (actor <see cref="PriceAlertChecker.LiveActor"/>); a tick before the open is indicative and triggers nothing;</item>
/// <item>clears the Action center's done decisions whose condition ended, like the quote poll does after its alert check, when a price or an alert changed - at most once a minute (<see cref="ReconcileInterval"/>; the evaluation
/// reads every holding), a triggered alert goes ahead at once, and a reconcile the throttle held back is done by a later flush, even one without ticks;</item>
/// <item>sends <c>NOTIFY live_prices</c> after a write, at most once a second (a notification held back by the throttle goes out with the next flush, even one without ticks; it is owed from the moment the holdings write succeeded, so a later failure does not lose it).</item>
/// </list>
/// Nothing is written, read or notified when nothing changed. A failure keeps the batch for the next flush (fail closed: the 5-minute quotes carry on). Lives for the lifetime of the feed process (a singleton); the ports
/// are resolved per flush. Nothing secret is logged.
/// </summary>
internal sealed class LiveFlush(IServiceScopeFactory scopes, TimeProvider clock, ILogger<LiveFlush> logger) : ILiveFlush
{
    /// <summary>The shortest time between two <c>live_prices</c> notifications.</summary>
    public static readonly TimeSpan NotifyInterval = TimeSpan.FromSeconds(1);

    /// <summary>The shortest time between two reconciliations of the Action center's decisions (an alert that triggers does not wait).</summary>
    public static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(1);

    private readonly TickCoalescer _ticks = new();
    private long _lastNotify = -1;
    private bool _notifyPending;
    private long _lastReconcile = -1;
    private bool _reconcilePending;

    public void Add(LivePrice tick) => _ticks.Add(tick);

    public void Reset()
    {
        _ticks.Reset();
        _notifyPending = false;
        _reconcilePending = false;
        _lastNotify = -1;
        _lastReconcile = -1;
    }

    public async ValueTask<LiveFlushResult> FlushAsync(CancellationToken cancellationToken)
    {
        var batch = _ticks.Drain();
        if (batch.Count == 0)
        {
            // Nothing to write; only what the throttles held back last time may be due.
            if (_reconcilePending)
            {
                await ReconcileIfDueAsync(null, force: false, cancellationToken);
            }

            return _notifyPending ? await NotifyIfDueAsync(null, cancellationToken) : LiveFlushResult.Nothing();
        }

        var started = clock.GetTimestamp();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var prices = services.GetRequiredService<ILivePrices>();

            var held = await prices.ListHeldInstrumentsAsync(cancellationToken);
            var holdingTicks = batch.Where(t => held.Contains(t.InstrumentId)).ToList();
            var otherTicks = batch.Where(t => !held.Contains(t.InstrumentId)).ToList();
            var updated = holdingTicks.Count == 0 ? 0 : await prices.ApplyAsync(holdingTicks, cancellationToken);
            var written = otherTicks.Count == 0 ? 0 : await services.GetRequiredService<ILiveQuotes>().UpsertAsync(otherTicks, cancellationToken);

            if (updated > 0 || written > 0)
            {
                _notifyPending = true; // owed from here on: a failure of the alert step below must not lose it
            }

            var triggered = await CheckAlertsAsync(services, batch, cancellationToken);
            if (triggered > 0)
            {
                LiveLog.AlertsTriggered(logger, triggered);
            }

            if (updated > 0 || triggered > 0)
            {
                _reconcilePending = true;
                await ReconcileIfDueAsync(services, force: triggered > 0, cancellationToken);
            }

            var result = await NotifyIfDueAsync(services.GetRequiredService<ILiveNotifier>(), cancellationToken);
            result = result with { Ticks = batch.Count, HoldingsUpdated = updated, QuotesWritten = written, AlertsTriggered = triggered };
            var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
            LiveLog.Flushed(logger, result.Ticks, updated, written, triggered, result.Notified, elapsedMs);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ticks.Requeue(batch);
            throw;
        }
#pragma warning disable CA1031 // fail closed: a failed flush must not stop the feed; the batch goes back and the next flush tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _ticks.Requeue(batch);
            LiveLog.FlushFailed(logger, batch.Count, ex);
            return new LiveFlushResult(batch.Count, 0, 0, 0, false, true);
        }
    }

    /// <summary>The active alerts of the flushed instruments, checked against their tick prices. Indicative ticks (before 09:15 India time) trigger nothing.</summary>
    private async ValueTask<int> CheckAlertsAsync(IServiceProvider services, IReadOnlyList<LivePrice> batch, CancellationToken cancellationToken)
    {
        var firm = batch.Where(t => !LivePricePolicy.IsIndicative(t.TickAt)).ToList();
        if (firm.Count == 0)
        {
            return 0;
        }

        var checker = services.GetRequiredService<PriceAlertChecker>();
        var prices = firm.ToDictionary(t => t.InstrumentId, t => t.Price);
        var active = (await checker.ListActiveAsync(cancellationToken)).Where(a => prices.ContainsKey(a.InstrumentId)).ToList();
        if (active.Count == 0)
        {
            return 0;
        }

        // The alert is stamped with the time of the newest tick of the batch (never later than now).
        var at = new[] { firm.Max(t => t.TickAt), clock.GetUtcNow() }.Min();
        return await checker.CheckAsync(active, prices, at, PriceAlertChecker.LiveActor, cancellationToken);
    }

    /// <summary>Reconciles the Action center's decisions when one is owed and the last one is a minute old (or an alert triggered).</summary>
    private async ValueTask ReconcileIfDueAsync(IServiceProvider? services, bool force, CancellationToken cancellationToken)
    {
        var now = clock.GetTimestamp();
        if (!_reconcilePending || (!force && _lastReconcile >= 0 && clock.GetElapsedTime(_lastReconcile, now) < ReconcileInterval))
        {
            return;
        }

        try
        {
            if (services is null)
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ActionReconciler>().ReconcileAsync("live", cancellationToken);
            }
            else
            {
                await services.GetRequiredService<ActionReconciler>().ReconcileAsync("live", cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // only an empty flush gets here without the flush's own guard; the reconcile stays owed and is tried again
        catch (Exception ex) when (services is null)
#pragma warning restore CA1031
        {
            LiveLog.FlushFailed(logger, 0, ex);
            return;
        }

        _lastReconcile = now;
        _reconcilePending = false;
    }

    private async ValueTask<LiveFlushResult> NotifyIfDueAsync(ILiveNotifier? notifier, CancellationToken cancellationToken)
    {
        var now = clock.GetTimestamp();
        if (!_notifyPending || (_lastNotify >= 0 && clock.GetElapsedTime(_lastNotify, now) < NotifyInterval))
        {
            return LiveFlushResult.Nothing();
        }

        if (notifier is null)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILiveNotifier>().PricesChangedAsync(cancellationToken);
        }
        else
        {
            await notifier.PricesChangedAsync(cancellationToken);
        }

        _lastNotify = now;
        _notifyPending = false;
        return LiveFlushResult.Nothing(notified: true);
    }
}
