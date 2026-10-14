using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Application.Portfolio.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Portfolio.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Live;

/// <summary>
/// The live price sink (ADR 0024, P2-10 criteria 9, 11, 13, 14) with fakes behind the ports and the real alert checker over the in-memory alert data: nothing changed means nothing written or read, the split between instruments the
/// holdings hold and the others, alerts triggered once with the live actor, indicative ticks, the throttled notification and a failed flush. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
public sealed class LiveFlushTests : IDisposable
{
    private readonly ActionsHarness _actions = new();
    private readonly FakeLivePrices _prices = new();
    private readonly FakeLiveQuotes _quotes = new();
    private readonly FakeLiveNotifier _notifier = new();
    private readonly MiniProvider _provider;
    private readonly LiveFlush _flush;

    public LiveFlushTests()
    {
        _provider = new MiniProvider()
            .Singleton<ILivePrices>(_prices)
            .Singleton<ILiveQuotes>(_quotes)
            .Singleton<ILiveNotifier>(_notifier)
            .Singleton(_actions.Checker())
            .Singleton(_actions.Reconciler());
        _flush = new LiveFlush(_provider, _actions.Clock, NullLogger<LiveFlush>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _provider.Dispose();

    private static Guid Id(string symbol) => ActionFixtures.InstrumentOf(symbol);

    private LivePrice Tick(string symbol, decimal price, double secondsAfterNow = -1) => new(Id(symbol), price, _actions.Now.AddSeconds(secondsAfterNow));

    // ---- nothing changed ----

    [Fact]
    public async Task A_flush_with_no_tick_reads_and_writes_nothing()
    {
        var result = await _flush.FlushAsync(Ct);

        result.ShouldBe(LiveFlushResult.Nothing());
        (_prices.Lists, _prices.Applied.Count, _quotes.Upserts.Count, _notifier.Prices, _actions.Data.StateReads).ShouldBe((0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task A_second_flush_without_a_new_tick_writes_nothing()
    {
        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m));
        await _flush.FlushAsync(Ct);

        var second = await _flush.FlushAsync(Ct);

        second.Ticks.ShouldBe(0);
        _prices.Applied.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_tick_that_is_not_newer_than_the_flushed_one_is_dropped_so_nothing_is_written()
    {
        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m, -2));
        await _flush.FlushAsync(Ct);

        _flush.Add(Tick("INFY", 1499m, -3)); // late
        _flush.Add(Tick("INFY", 1500m, -2)); // the same tick again
        var result = await _flush.FlushAsync(Ct);

        result.Ticks.ShouldBe(0);
        _prices.Applied.Count.ShouldBe(1);
    }

    // ---- holdings and the others ----

    [Fact]
    public async Task Held_instruments_go_to_the_holdings_in_one_call_and_the_others_to_live_quotes()
    {
        _prices.Held.Add(Id("INFY"));
        _prices.Held.Add(Id("TCS"));
        _flush.Add(Tick("INFY", 1500m));
        _flush.Add(Tick("INFY", 1501m, -0.5)); // the latest of the instrument
        _flush.Add(Tick("TCS", 4000m));
        _flush.Add(Tick("NIFTY", 24000m));

        var result = await _flush.FlushAsync(Ct);

        _prices.Applied.ShouldHaveSingleItem().Select(t => (t.InstrumentId, t.Price)).Order().ShouldBe([(Id("INFY"), 1501m), (Id("TCS"), 4000m)], ignoreOrder: true);
        _quotes.Upserts.ShouldHaveSingleItem().Select(t => (t.InstrumentId, t.Price)).ShouldBe([(Id("NIFTY"), 24000m)]);
        (result.Ticks, result.HoldingsUpdated, result.QuotesWritten, result.Failed).ShouldBe((3, 2, 1, false));
    }

    [Fact]
    public async Task Only_non_holding_ticks_make_no_call_to_the_holdings_and_only_holding_ticks_none_to_live_quotes()
    {
        _flush.Add(Tick("NIFTY", 24000m));
        await _flush.FlushAsync(Ct);
        _prices.Applied.ShouldBeEmpty();

        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m));
        await _flush.FlushAsync(Ct);
        _quotes.Upserts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_done_decisions_are_reconciled_after_a_holding_price_changed_but_not_after_live_quotes_only()
    {
        _flush.Add(Tick("NIFTY", 24000m));
        await _flush.FlushAsync(Ct);
        _actions.Data.StateReads.ShouldBe(0);

        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m));
        await _flush.FlushAsync(Ct);

        _actions.Data.StateReads.ShouldBe(1);
    }

    [Fact]
    public async Task The_reconcile_runs_at_most_once_a_minute_and_the_one_held_back_is_done_by_a_later_flush_even_without_ticks()
    {
        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m, -10));
        await _flush.FlushAsync(Ct);
        _actions.Advance(TimeSpan.FromSeconds(20));
        _flush.Add(Tick("INFY", 1501m, -1));
        await _flush.FlushAsync(Ct);

        _actions.Data.StateReads.ShouldBe(1); // the second flush wrote, but the evaluation is a minute apart at the least

        _actions.Advance(TimeSpan.FromSeconds(30));
        await _flush.FlushAsync(Ct);
        _actions.Data.StateReads.ShouldBe(1);

        _actions.Advance(TimeSpan.FromSeconds(20)); // 70 s since the first
        await _flush.FlushAsync(Ct); // no tick: the owed reconcile is done
        _actions.Data.StateReads.ShouldBe(2);
        _actions.Advance(TimeSpan.FromMinutes(5));
        await _flush.FlushAsync(Ct);
        _actions.Data.StateReads.ShouldBe(2); // nothing is owed
    }

    [Fact]
    public async Task A_triggered_alert_reconciles_at_once_whatever_the_throttle()
    {
        _prices.Held.Add(Id("INFY"));
        Alert("INFY", AlertDirection.AtOrBelow, 1200m);
        _flush.Add(Tick("INFY", 1500m, -10));
        await _flush.FlushAsync(Ct);
        _actions.Data.StateReads.ShouldBe(1);
        _actions.Advance(TimeSpan.FromSeconds(2));

        _flush.Add(Tick("INFY", 1190m, -1));
        var result = await _flush.FlushAsync(Ct);

        (result.AlertsTriggered, _actions.Data.StateReads).ShouldBe((1, 2));
    }

    // ---- alerts ----

    private PriceAlert Alert(string symbol, AlertDirection direction, decimal level)
    {
        _actions.Data.AddInstrument(symbol);
        return _actions.Data.SeedAlert(Id(symbol), direction, level, _actions.Now);
    }

    [Fact]
    public async Task An_alert_hit_by_a_tick_triggers_once_with_the_live_price_and_the_live_actor()
    {
        var alert = Alert("INFY", AlertDirection.AtOrBelow, 1200m);
        _flush.Add(Tick("INFY", 1190.5m, -1));

        var result = await _flush.FlushAsync(Ct);

        result.AlertsTriggered.ShouldBe(1);
        var stored = _actions.Data.AlertOf(alert.Id);
        (stored.Status, stored.TriggerPrice, stored.TriggeredAt).ShouldBe((AlertStatus.Triggered, 1190.5m, _actions.Now.AddSeconds(-1)));
        var audit = _actions.Data.Events.ShouldHaveSingleItem();
        (audit.Actor, audit.Event).ShouldBe((PriceAlertChecker.LiveActor, XDrishti.Domain.Accounts.AccountEventType.PriceAlertTriggered));
        PriceAlertChecker.LiveActor.ShouldNotBe(PriceAlertChecker.Actor);
    }

    [Fact]
    public async Task A_second_tick_past_the_level_does_not_trigger_the_alert_again()
    {
        Alert("INFY", AlertDirection.AtOrBelow, 1200m);
        _flush.Add(Tick("INFY", 1190m, -3));
        await _flush.FlushAsync(Ct);

        _flush.Add(Tick("INFY", 1180m, -1));
        var second = await _flush.FlushAsync(Ct);

        second.AlertsTriggered.ShouldBe(0);
        _actions.Data.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_alert_the_tick_does_not_reach_stays_active()
    {
        var alert = Alert("INFY", AlertDirection.AtOrBelow, 1200m);
        _flush.Add(Tick("INFY", 1200.01m));

        var result = await _flush.FlushAsync(Ct);

        result.AlertsTriggered.ShouldBe(0);
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Active);
    }

    [Fact]
    public async Task An_alert_on_an_instrument_with_no_tick_in_the_flush_is_not_evaluated()
    {
        var other = Alert("TCS", AlertDirection.AtOrBelow, 5000m);
        _flush.Add(Tick("INFY", 10m));

        var result = await _flush.FlushAsync(Ct);

        result.AlertsTriggered.ShouldBe(0);
        _actions.Data.AlertOf(other.Id).Status.ShouldBe(AlertStatus.Active);
    }

    [Fact]
    public async Task An_alert_only_instrument_is_written_to_live_quotes_and_still_triggers()
    {
        var alert = Alert("TCS", AlertDirection.AtOrAbove, 4000m);
        _flush.Add(Tick("TCS", 4001m));

        var result = await _flush.FlushAsync(Ct);

        (result.QuotesWritten, result.AlertsTriggered).ShouldBe((1, 1));
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Triggered);
    }

    [Fact]
    public async Task An_indicative_tick_before_the_open_is_written_but_triggers_no_alert()
    {
        _prices.Held.Add(Id("INFY"));
        var alert = Alert("INFY", AlertDirection.AtOrBelow, 1200m);
        _flush.Add(new LivePrice(Id("INFY"), 1000m, new DateTimeOffset(2026, 10, 12, 3, 40, 0, TimeSpan.Zero))); // 09:10 India time

        var result = await _flush.FlushAsync(Ct);

        (result.HoldingsUpdated, result.AlertsTriggered).ShouldBe((1, 0));
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Active);
    }

    // ---- the notification ----

    [Fact]
    public async Task A_flush_that_wrote_notifies_and_one_that_wrote_nothing_does_not()
    {
        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m));

        var written = await _flush.FlushAsync(Ct);
        _actions.Advance(TimeSpan.FromSeconds(5));
        var idle = await _flush.FlushAsync(Ct);

        (written.Notified, idle.Notified, _notifier.Prices).ShouldBe((true, false, 1));
    }

    [Fact]
    public async Task Notifications_are_at_most_one_a_second_and_a_held_back_one_goes_out_with_the_next_flush()
    {
        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m, -10));
        await _flush.FlushAsync(Ct);
        _actions.Advance(TimeSpan.FromMilliseconds(400));
        _flush.Add(Tick("INFY", 1501m, -9));

        var held = await _flush.FlushAsync(Ct);

        (held.Notified, held.HoldingsUpdated, _notifier.Prices).ShouldBe((false, 1, 1));

        _actions.Advance(TimeSpan.FromMilliseconds(700));
        var later = await _flush.FlushAsync(Ct); // no tick, but a notification is owed

        (later.Notified, later.Ticks, _notifier.Prices).ShouldBe((true, 0, 2));
        _actions.Advance(TimeSpan.FromSeconds(5));
        (await _flush.FlushAsync(Ct)).Notified.ShouldBeFalse();
    }

    // ---- failure ----

    [Fact]
    public async Task A_failed_write_keeps_the_batch_and_the_next_flush_writes_it()
    {
        _prices.Held.Add(Id("INFY"));
        _prices.FailNext = new InvalidOperationException("database unavailable");
        _flush.Add(Tick("INFY", 1500m));

        var failed = await _flush.FlushAsync(Ct);

        (failed.Failed, failed.Notified, failed.HoldingsUpdated).ShouldBe((true, false, 0));
        _notifier.Prices.ShouldBe(0);

        var retried = await _flush.FlushAsync(Ct);

        (retried.Failed, retried.HoldingsUpdated, _prices.Applied.Count).ShouldBe((false, 1, 1));
    }

    [Fact]
    public async Task A_notification_owed_for_a_holdings_write_is_not_lost_when_the_alert_step_fails()
    {
        _prices.Held.Add(Id("INFY"));
        Alert("INFY", AlertDirection.AtOrBelow, 1200m);
        _actions.Data.RefuseNextSave = true; // the trigger's audit event cannot be saved
        _flush.Add(Tick("INFY", 1190m, -1));

        var failed = await _flush.FlushAsync(Ct);

        (failed.Failed, _notifier.Prices).ShouldBe((true, 0));
        _actions.Advance(TimeSpan.FromSeconds(5));
        var later = await _flush.FlushAsync(Ct); // the batch is back and the alert triggers now; the holding was written already (nothing new), the notification was owed

        (later.Notified, later.AlertsTriggered, _notifier.Prices).ShouldBe((true, 1, 1));
    }

    [Fact]
    public async Task A_new_window_starts_clean_after_reset()
    {
        _prices.Held.Add(Id("INFY"));
        _flush.Add(Tick("INFY", 1500m, -1));
        _flush.Reset();

        (await _flush.FlushAsync(Ct)).Ticks.ShouldBe(0);
        _flush.Add(Tick("INFY", 1400m, -50)); // an older time than before the reset is accepted again
        (await _flush.FlushAsync(Ct)).Ticks.ShouldBe(1);
    }

    [Fact]
    public async Task A_flush_cancelled_keeps_the_batch()
    {
        _prices.Held.Add(Id("INFY"));
        _prices.FailNext = new OperationCanceledException();
        _flush.Add(Tick("INFY", 1500m));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await _flush.FlushAsync(cts.Token));

        (await _flush.FlushAsync(Ct)).Ticks.ShouldBe(1);
    }
}
