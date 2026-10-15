using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Application.Portfolio.Quotes;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Portfolio.Actions;
using XDrishti.Application.Tests.Portfolio.Heal;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Platform.Services;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Quotes;

/// <summary>
/// The price alerts in the quote poll (acceptance criterion 15 of P2-09): the alert instruments join the quoted set in one request, every alert hit by the answer is triggered once with its audit event, an instrument without a quote stays active, a failed run triggers
/// nothing, the message counts the triggered alerts, and the Action center's done decisions are reconciled after the tick. Monday 2026-10-12, India time.
/// </summary>
public sealed class HoldingQuotesAlertsTests : IDisposable
{
    private readonly FakeHoldingQuotes _holdings = new();
    private readonly FakeQuoteSource _source = new();
    private readonly FakeDataAccount _account = FakeDataAccount.Connected();
    private readonly ActionsHarness _actions = new();
    private readonly InMemorySyncHealth _health = new();
    private readonly FixedHealSettings _heal = new();
    private readonly ServicesWorld _world;

    public HoldingQuotesAlertsTests() =>
        _world = new ServicesWorld(
            (world, scopes) => [new HoldingQuotesService(scopes, world.Clock, NullLogger<HoldingQuotesService>.Instance)],
            provider => provider
                .Singleton<IHoldingQuotes>(_holdings)
                .Singleton<IQuoteSource>(_source)
                .Singleton<IDataAccountStatus>(_account)
                .Singleton(_actions.Checker())
                .Singleton(_actions.Reconciler())
                .Singleton<ISyncHealthStore>(_health)
                .Singleton<IHealSettings>(_heal)
                .Singleton<SyncHealthRecorder, SyncHealthRecorder>());

    public void Dispose() => _world.Dispose();

    private static Guid Held(int i) => Guid.Parse($"00000000-0000-7000-8000-{i:D12}");

    private async Task<ServiceRun> RunAsync()
    {
        var started = await _world.RunNowAsync(HoldingQuotesService.Id);
        started.IsSuccess.ShouldBeTrue();
        await _world.Runner.WhenIdleAsync();
        return _world.Data.RunsOf(HoldingQuotesService.Id).Last();
    }

    /// <summary>An alert on an instrument of the fake master with the given Dhan security id.</summary>
    private PriceAlert Alert(Guid instrumentId, string symbol, string? securityId, AlertDirection direction, decimal level)
    {
        _actions.Data.Instruments[instrumentId] = new AlertInstrument(instrumentId, symbol, symbol + " Ltd", "share", "NSE", false, securityId is not null);
        if (securityId is not null)
        {
            _actions.Data.SecurityIds[instrumentId] = securityId;
        }

        return _actions.Data.SeedAlert(instrumentId, direction, level, _world.Clock.GetUtcNow());
    }

    private static SecurityQuote Quote(string securityId, decimal price) => new(securityId, price, price - 1m);

    [Fact]
    public async Task The_securities_of_the_alerts_join_the_holdings_in_one_request_each_once()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 2, nse: 2); // securities 1001 and 1002
        Alert(Guid.NewGuid(), "NEW", "7777", AlertDirection.AtOrBelow, 10m);
        Alert(Held(1), "HELD", "1001", AlertDirection.AtOrAbove, 1000m); // an alert on a security that is held: quoted once

        await RunAsync();

        _source.Calls.ShouldHaveSingleItem().OrderBy(i => i).ShouldBe(["1001", "1002", "7777"]);
    }

    [Fact]
    public async Task A_hit_alert_is_triggered_audited_and_counted_in_the_message()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 2, nse: 2);
        var alert = Alert(Guid.NewGuid(), "NEW", "7777", AlertDirection.AtOrBelow, 100m);
        _source.Respond = ids => new QuoteResult([.. ids.Select(i => i == "7777" ? Quote(i, 99.5m) : Quote(i, 100m))], null, 1);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "Quoted 2 of 2 holdings; 0 not on NSE; 1 alert triggered"));
        var stored = _actions.Data.AlertOf(alert.Id);
        (stored.Status, stored.TriggerPrice, stored.TriggeredAt).ShouldBe((AlertStatus.Triggered, 99.5m, _world.Clock.GetUtcNow()));
        _actions.Data.Events.Single().Event.ShouldBe(AccountEventType.PriceAlertTriggered);
    }

    [Fact]
    public async Task Several_triggered_alerts_are_counted_in_the_plural()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        Alert(Guid.NewGuid(), "A", "7001", AlertDirection.AtOrBelow, 200m);
        Alert(Guid.NewGuid(), "B", "7002", AlertDirection.AtOrAbove, 50m);

        var run = await RunAsync();

        run.Message.ShouldBe("Quoted 1 of 1 holdings; 0 not on NSE; 2 alerts triggered");
    }

    [Fact]
    public async Task The_quotes_of_alert_only_securities_are_not_written_into_the_holdings()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        Alert(Guid.NewGuid(), "NEW", "7777", AlertDirection.AtOrBelow, 10m);

        await RunAsync();

        _holdings.Applied.ShouldHaveSingleItem().Quotes.Select(q => q.InstrumentId).ShouldBe([Held(1)]);
    }

    [Fact]
    public async Task An_alert_whose_instrument_has_no_quote_in_the_answer_stays_active()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        var missing = Alert(Guid.NewGuid(), "GONE", "7777", AlertDirection.AtOrBelow, 100m);
        _source.Respond = ids => new QuoteResult([.. ids.Where(i => i != "7777").Select(i => Quote(i, 100m))], null, 1);

        var run = await RunAsync();

        run.Message.ShouldBe("Quoted 1 of 1 holdings; 0 not on NSE");
        _actions.Data.AlertOf(missing.Id).Status.ShouldBe(AlertStatus.Active);
    }

    [Fact]
    public async Task A_failed_run_triggers_nothing()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        var alert = Alert(Guid.NewGuid(), "NEW", "7777", AlertDirection.AtOrBelow, 100m);
        _source.Respond = _ => QuoteResult.Failed(new QuoteFailure(QuoteFailureKind.Failed, "Dhan's quote service answered HTTP 503."), 1);

        var run = await RunAsync();

        run.Status.ShouldBe(ServiceRunStatus.Skipped); // Dhan not answering is tried again by the next poll and is not shown (ADR 0027)
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Active);
        _actions.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_without_a_usable_price_triggers_nothing()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        var alert = Alert(Guid.NewGuid(), "NEW", "7777", AlertDirection.AtOrBelow, 100m);
        _source.Respond = _ => new QuoteResult([], null, 1);

        var run = await RunAsync();

        run.Status.ShouldBe(ServiceRunStatus.Failed);
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Active);
    }

    [Fact]
    public async Task Without_holdings_the_alerts_are_still_quoted_and_checked()
    {
        var alert = Alert(Guid.NewGuid(), "NEW", "7777", AlertDirection.AtOrBelow, 100m);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "No open holdings; 1 alert triggered"));
        _source.Calls.ShouldHaveSingleItem().ShouldBe(["7777"]);
        _holdings.Applied.ShouldBeEmpty();
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Triggered);
    }

    [Fact]
    public async Task An_alert_that_dhan_cannot_quote_is_never_asked_for_and_stays_active()
    {
        var alert = Alert(Guid.NewGuid(), "ODD", null, AlertDirection.AtOrBelow, 100m);

        var run = await RunAsync();

        (run.Status, run.Message).ShouldBe((ServiceRunStatus.Succeeded, "No open holdings"));
        _source.Calls.ShouldBeEmpty();
        _actions.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Active);
    }

    [Fact]
    public async Task Cancelled_and_triggered_alerts_are_not_quoted()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);
        var cancelled = Alert(Guid.NewGuid(), "C", "7001", AlertDirection.AtOrBelow, 200m);
        var triggered = Alert(Guid.NewGuid(), "T", "7002", AlertDirection.AtOrBelow, 200m);
        await _actions.Data.TryCancelAsync(cancelled.Id, TestContext.Current.CancellationToken);
        await _actions.Data.TryTriggerAsync(triggered.Id, 1m, _world.Clock.GetUtcNow(), TestContext.Current.CancellationToken);

        await RunAsync();

        _source.Calls.ShouldHaveSingleItem().ShouldBe(["1001"]);
    }

    [Fact]
    public async Task Without_alerts_the_run_is_what_it_was()
    {
        _holdings.Targets = FakeHoldingQuotes.Held(open: 43, nse: 41);

        var run = await RunAsync();

        run.Message.ShouldBe("Quoted 41 of 43 holdings; 2 not on NSE");
        _actions.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task After_the_tick_the_done_decisions_whose_condition_ended_are_cleared()
    {
        var holding = ActionFixtures.Pos("BAD", 120m); // no longer below cost
        var harness = new ActionsHarness(holding);
        await harness.Data.SetAsync("below_cost", holding.Id.ToString(), ActionDecision.Done, null, "owner@example.com", harness.Now, TestContext.Current.CancellationToken);
        await harness.Data.SaveAsync(TestContext.Current.CancellationToken);
        using var world = new ServicesWorld(
            (w, scopes) => [new HoldingQuotesService(scopes, w.Clock, NullLogger<HoldingQuotesService>.Instance)],
            provider => provider
                .Singleton<IHoldingQuotes>(_holdings)
                .Singleton<IQuoteSource>(_source)
                .Singleton<IDataAccountStatus>(_account)
                .Singleton(harness.Checker())
                .Singleton(harness.Reconciler())
                .Singleton<ISyncHealthStore>(_health)
                .Singleton<IHealSettings>(_heal)
                .Singleton<SyncHealthRecorder, SyncHealthRecorder>());
        _holdings.Targets = FakeHoldingQuotes.Held(open: 1, nse: 1);

        await world.RunNowAsync(HoldingQuotesService.Id);
        await world.Runner.WhenIdleAsync();

        harness.Data.StateOf("below_cost", holding.Id.ToString())!.State.ShouldBe(ActionDecision.Cleared);
    }
}
