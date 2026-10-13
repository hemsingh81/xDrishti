using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>
/// Triggering from a quote answer (acceptance criterion 15): both directions, equality, an instrument without a quote, once only under overlapping runs or retries, and the change committed together with its audit event.
/// </summary>
public sealed class PriceAlertCheckerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ActionsHarness _harness = new();

    private Guid Instrument(string symbol)
    {
        var instrument = _harness.Data.AddInstrument(symbol);
        return instrument.Id;
    }

    private async Task<int> CheckAsync(IEnumerable<(Guid Instrument, decimal Price)> quotes, IReadOnlyList<ActiveAlert>? active = null)
    {
        var checker = _harness.Checker();
        var list = active ?? await checker.ListActiveAsync(Ct);
        return await checker.CheckAsync(list, quotes.ToDictionary(q => q.Instrument, q => q.Price), _harness.Now, Ct);
    }

    [Theory]
    [InlineData(AlertDirection.AtOrBelow, 1200, 1199.99, true)]
    [InlineData(AlertDirection.AtOrBelow, 1200, 1200, true)] // equality
    [InlineData(AlertDirection.AtOrBelow, 1200, 1200.01, false)]
    [InlineData(AlertDirection.AtOrAbove, 1200, 1200.01, true)]
    [InlineData(AlertDirection.AtOrAbove, 1200, 1200, true)] // equality
    [InlineData(AlertDirection.AtOrAbove, 1200, 1199.99, false)]
    public async Task The_condition_is_evaluated_on_the_last_price_of_the_tick_and_equality_counts(AlertDirection direction, double level, double price, bool triggers)
    {
        var infy = Instrument("INFY");
        var alert = _harness.Data.SeedAlert(infy, direction, (decimal)level, _harness.Now);

        var triggered = await CheckAsync([(infy, (decimal)price)]);

        triggered.ShouldBe(triggers ? 1 : 0);
        _harness.Data.AlertOf(alert.Id).Status.ShouldBe(triggers ? AlertStatus.Triggered : AlertStatus.Active);
    }

    [Fact]
    public async Task A_triggered_alert_keeps_the_trigger_price_and_time_and_is_audited_with_the_system_as_actor()
    {
        var infy = Instrument("INFY");
        var alert = _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);
        _harness.Advance(TimeSpan.FromMinutes(5));

        await CheckAsync([(infy, 1190.5m)]);

        var stored = _harness.Data.AlertOf(alert.Id);
        (stored.Status, stored.TriggerPrice, stored.TriggeredAt).ShouldBe((AlertStatus.Triggered, 1190.5m, _harness.Now));
        var audit = _harness.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.AccountId, audit.At).ShouldBe((AccountEventType.PriceAlertTriggered, "system:holdings-quotes", null, _harness.Now));
        audit.Detail.ShouldBe("Price alert triggered: INFY at or below ₹1,200 (last price ₹1,190.5)");
    }

    [Fact]
    public async Task An_instrument_without_a_quote_in_the_answer_stays_active()
    {
        var infy = Instrument("INFY");
        var tcs = Instrument("TCS");
        var quoted = _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);
        var missing = _harness.Data.SeedAlert(tcs, AlertDirection.AtOrBelow, 5000m, _harness.Now);

        var triggered = await CheckAsync([(infy, 1000m)]);

        triggered.ShouldBe(1);
        _harness.Data.AlertOf(quoted.Id).Status.ShouldBe(AlertStatus.Triggered);
        _harness.Data.AlertOf(missing.Id).Status.ShouldBe(AlertStatus.Active);
    }

    [Fact]
    public async Task A_quote_without_a_positive_price_triggers_nothing()
    {
        var infy = Instrument("INFY");
        _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);

        (await CheckAsync([(infy, 0m)])).ShouldBe(0);
    }

    [Fact]
    public async Task Several_alerts_on_one_instrument_trigger_independently()
    {
        var infy = Instrument("INFY");
        var below = _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);
        var above = _harness.Data.SeedAlert(infy, AlertDirection.AtOrAbove, 1500m, _harness.Now);
        var farBelow = _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 900m, _harness.Now);

        var triggered = await CheckAsync([(infy, 1100m)]);

        triggered.ShouldBe(1);
        (_harness.Data.AlertOf(below.Id).Status, _harness.Data.AlertOf(above.Id).Status, _harness.Data.AlertOf(farBelow.Id).Status)
            .ShouldBe((AlertStatus.Triggered, AlertStatus.Active, AlertStatus.Active));
    }

    [Fact]
    public async Task An_alert_triggers_at_most_once_even_when_two_runs_overlap()
    {
        var infy = Instrument("INFY");
        _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);
        var checker = _harness.Checker();
        var seenByBoth = await checker.ListActiveAsync(Ct); // both runs read the alert while it was active

        var first = await checker.CheckAsync(seenByBoth, new Dictionary<Guid, decimal> { [infy] = 1100m }, _harness.Now, Ct);
        var second = await checker.CheckAsync(seenByBoth, new Dictionary<Guid, decimal> { [infy] = 1090m }, _harness.Now, Ct);

        (first, second).ShouldBe((1, 0));
        _harness.Data.Events.Count.ShouldBe(1);
        _harness.Data.Alerts.Single().TriggerPrice.ShouldBe(1100m);
    }

    [Fact]
    public async Task A_retry_of_the_same_answer_changes_nothing()
    {
        var infy = Instrument("INFY");
        _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);

        (await CheckAsync([(infy, 1100m)])).ShouldBe(1);
        (await CheckAsync([(infy, 1100m)])).ShouldBe(0);

        _harness.Data.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_alert_cancelled_after_the_run_read_it_is_not_triggered()
    {
        var infy = Instrument("INFY");
        var alert = _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);
        var checker = _harness.Checker();
        var active = await checker.ListActiveAsync(Ct);
        await _harness.Data.TryCancelAsync(alert.Id, Ct);

        var triggered = await checker.CheckAsync(active, new Dictionary<Guid, decimal> { [infy] = 1000m }, _harness.Now, Ct);

        triggered.ShouldBe(0);
        _harness.Data.AlertOf(alert.Id).Status.ShouldBe(AlertStatus.Cancelled);
        _harness.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_change_and_its_audit_event_commit_together_or_not_at_all()
    {
        var infy = Instrument("INFY");
        var tcs = Instrument("TCS");
        var a = _harness.Data.SeedAlert(infy, AlertDirection.AtOrBelow, 1200m, _harness.Now);
        var b = _harness.Data.SeedAlert(tcs, AlertDirection.AtOrBelow, 5000m, _harness.Now);
        _harness.Data.RefuseNextSave = true; // the audit events cannot be saved

        await Should.ThrowAsync<InvalidOperationException>(async () => await CheckAsync([(infy, 1000m), (tcs, 4000m)]));

        (_harness.Data.AlertOf(a.Id).Status, _harness.Data.AlertOf(b.Id).Status).ShouldBe((AlertStatus.Active, AlertStatus.Active));
        _harness.Data.Events.ShouldBeEmpty();
        (await CheckAsync([(infy, 1000m), (tcs, 4000m)])).ShouldBe(2); // the next tick tries again
    }

    [Fact]
    public async Task Nothing_to_check_costs_nothing()
    {
        var checker = _harness.Checker();

        (await checker.CheckAsync([], new Dictionary<Guid, decimal>(), _harness.Now, Ct)).ShouldBe(0);
        _harness.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task The_active_alerts_are_listed_with_their_dhan_security_id_or_none()
    {
        var infy = _harness.Data.AddInstrument("INFY");
        var odd = _harness.Data.AddInstrument("ODD", quotable: false);
        _harness.Data.SeedAlert(infy.Id, AlertDirection.AtOrBelow, 1m, _harness.Now);
        _harness.Data.SeedAlert(odd.Id, AlertDirection.AtOrBelow, 1m, _harness.Now);
        _harness.Data.SecurityIds[infy.Id] = "1594";

        var active = await _harness.Checker().ListActiveAsync(Ct);

        active.Select(a => (a.Symbol, a.SecurityId)).ShouldBe([("INFY", "1594"), ("ODD", null)]);
    }
}
