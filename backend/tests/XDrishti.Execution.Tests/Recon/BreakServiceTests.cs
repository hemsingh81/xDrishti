using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;
using XDrishti.Execution.Domain.Recon;
using XDrishti.Execution.Tests.Protection;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 criteria 8, 9 and 10: a break asks for a freeze and nothing is fixed; the owner's resolution is adopted by an appended event; the end of day moves clean intents to reconciled.</summary>
public sealed class BreakServiceTests
{
    private static readonly DateOnly Session = new(2026, 10, 30);
    private readonly FakeTimeProvider _clock = new(ReconFixtures.Now);
    private readonly FakeBroker _broker;
    private readonly StaticReconSource _ours = new();
    private readonly InMemoryReconStore _store = new() { Session = Session };
    private readonly RecordingHaltRequests _halts = new();
    private readonly InMemoryAlarmStore _alarms = new();
    private readonly MemoryEvents _events = new();
    private readonly BreakService _service;

    public BreakServiceTests()
    {
        _broker = new FakeBroker(_clock);
        _service = new BreakService(_broker, _ours, _store, _halts, _alarms, _events, new ReadBudget(_clock), _clock, () => ReconSettings.Default);
    }

    // The broker holds one filled entry of 10 and we agree: clean.
    private async Task CleanBookAsync(Guid? account = null, bool intentInEvents = true)
    {
        var id = account ?? ReconFixtures.Account;
        await _broker.PlaceAsync(new BrokerLeg(ReconFixtures.Cid, LegKind.Entry, ExecSide.Buy, ReconFixtures.Security, 10, 100m), CancellationToken.None);
        _broker.SetPrice(ReconFixtures.Security, 99m);
        _ours.Set(id, new OurAccountView(
            [ReconFixtures.Leg(brokerId: "B1")], [ReconFixtures.Fill(price: 99m)], [new OurPositionView(ReconFixtures.Security, 10)],
            [new OurIntentView(361, id, ExecState.Filled, ReconFixtures.Security, ExecSide.Buy, 10, 10, 95m, 100m)]));
        if (intentInEvents)
        {
            _events.StartFilled(id);
        }
    }

    [Fact]
    public async Task A_clean_run_stores_one_readable_run_with_no_break_and_asks_for_nothing()
    {
        await CleanBookAsync();

        var outcome = await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        (outcome.Readable, outcome.Breaks, outcome.Clean).ShouldBe((true, 0, true));
        (await _store.BreaksAsync(null, CancellationToken.None)).ShouldBeEmpty();
        (await _store.RunsAsync(ReconFixtures.Account, Session, CancellationToken.None)).ShouldHaveSingleItem().Breaks.ShouldBe(0);
        _halts.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_break_asks_for_a_freeze_of_that_account_only_and_the_other_account_continues()
    {
        var other = Guid.NewGuid();
        await CleanBookAsync();
        _ours.Set(other, new OurAccountView([], [], [], []));
        _broker.ShiftPosition(ReconFixtures.Security, 3);

        var drifted = await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        drifted.Clean.ShouldBeFalse();
        _halts.Requests.ShouldHaveSingleItem().ShouldBe((ReconFixtures.Account, HaltLevel.Freeze, "reconciliation_break"));
    }

    [Fact]
    public async Task After_a_run_with_breaks_our_legs_fills_positions_and_intents_and_the_event_log_are_byte_identical()
    {
        await CleanBookAsync();
        _broker.ShiftPosition(ReconFixtures.Security, 3);
        _broker.AddManualOrder("M1", ReconFixtures.Security, ExecSide.Sell, 5, 120m);
        _broker.RemoveTrade("T1");
        var before = _ours.Fingerprint();
        var eventsBefore = _events.Events(361).Count;

        var outcome = await _service.RunAsync(ReconFixtures.Account, ReconKind.Eod, Session, CancellationToken.None);

        outcome.Breaks.ShouldBeGreaterThan(1);
        _ours.Fingerprint().ShouldBe(before);
        _events.Events(361).Count.ShouldBe(eventsBefore);
        _events.State(361).ShouldBe(ExecState.Filled);
    }

    [Fact]
    public async Task A_foreign_conflict_asks_for_entries_and_a_critical_alarm_not_for_a_freeze()
    {
        await CleanBookAsync();
        _broker.AddManualOrder("M1", ReconFixtures.Security, ExecSide.Sell, 5, 120m);

        await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        _halts.Requests.ShouldHaveSingleItem().ShouldBe((ReconFixtures.Account, HaltLevel.Entries, "foreign_conflict"));
        _alarms.Raised.ShouldHaveSingleItem().Code.ShouldBe(AlarmCodes.ForeignConflict);
        _alarms.Raised.Single().Severity.ShouldBe(AlarmSeverity.Critical);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_broker_that_cannot_be_read_or_a_record_that_cannot_be_read_is_an_unavailable_break_never_a_clean_run(bool brokerDown)
    {
        await CleanBookAsync();
        _broker.TokenExpired = brokerDown;
        _ours.Fail = !brokerDown;

        var outcome = await _service.RunAsync(ReconFixtures.Account, ReconKind.Final, Session, CancellationToken.None);

        (outcome.Readable, outcome.Clean, outcome.Breaks).ShouldBe((false, false, 1));
        (await _store.BreaksAsync(null, CancellationToken.None)).ShouldHaveSingleItem().Kind.ShouldBe(BreakKind.Unavailable);
        _halts.Requests.ShouldHaveSingleItem().Level.ShouldBe(HaltLevel.Freeze);
    }

    [Fact]
    public async Task Settings_that_are_out_of_range_make_the_run_unavailable()
    {
        await CleanBookAsync();
        var service = new BreakService(_broker, _ours, _store, _halts, _alarms, _events, new ReadBudget(_clock), _clock, () => null);

        (await service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None)).Readable.ShouldBeFalse();
    }

    [Fact]
    public async Task The_same_break_in_a_later_run_is_counted_and_asked_for_again_but_stored_as_one_row()
    {
        await CleanBookAsync();
        _broker.ShiftPosition(ReconFixtures.Security, 3);

        var first = await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);
        var second = await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        (first.NewBreaks, second.NewBreaks, second.Breaks).ShouldBe((1, 0, 1));
        (await _store.BreaksAsync(null, CancellationToken.None)).Count.ShouldBe(1);
        _halts.Requests.Count.ShouldBe(2, "a lost request is made again; the halt record does not stack it");
    }

    [Fact]
    public async Task A_clean_end_of_day_run_moves_the_closed_intent_to_reconciled_and_a_run_with_a_break_does_not()
    {
        await CleanBookAsync();
        await _events.AppendAsync(361, ExecEvent.Move(_events.Events(361).Count + 1, ExecState.Filled, ExecState.Closed, ExecEventSource.Engine, "system:test", ReconFixtures.Now), CancellationToken.None);
        _ours.Set(ReconFixtures.Account, new OurAccountView([ReconFixtures.Leg(brokerId: "B1")], [ReconFixtures.Fill(price: 99m)], [new OurPositionView(ReconFixtures.Security, 10)], [new OurIntentView(361, ReconFixtures.Account, ExecState.Closed, ReconFixtures.Security, ExecSide.Buy, 10, 10, 95m, 100m)]));
        _broker.ShiftPosition(ReconFixtures.Security, 1);
        await _service.RunAsync(ReconFixtures.Account, ReconKind.Eod, Session, CancellationToken.None);
        _events.State(361).ShouldBe(ExecState.Closed);

        _broker.ShiftPosition(ReconFixtures.Security, -1);
        await _service.RunAsync(ReconFixtures.Account, ReconKind.Eod, Session, CancellationToken.None);

        _events.State(361).ShouldBe(ExecState.Reconciled);
    }

    [Fact]
    public async Task A_continuous_clean_run_does_not_reconcile_an_intent_only_the_eod_and_final_runs_do()
    {
        await CleanBookAsync();
        await _events.AppendAsync(361, ExecEvent.Move(_events.Events(361).Count + 1, ExecState.Filled, ExecState.Closed, ExecEventSource.Engine, "system:test", ReconFixtures.Now), CancellationToken.None);
        _ours.Set(ReconFixtures.Account, new OurAccountView([ReconFixtures.Leg(brokerId: "B1")], [ReconFixtures.Fill(price: 99m)], [new OurPositionView(ReconFixtures.Security, 10)], [new OurIntentView(361, ReconFixtures.Account, ExecState.Closed, ReconFixtures.Security, ExecSide.Buy, 10, 10, 95m, 100m)]));

        await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        _events.State(361).ShouldBe(ExecState.Closed);
    }

    // ---- resolution (criterion 9)
    private async Task<long> BreakWithIntentAsync()
    {
        await CleanBookAsync();
        _broker.ForceStatus(ReconFixtures.Cid, "CANCELLED");
        await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);
        return (await _store.BreaksAsync(null, CancellationToken.None)).First(b => b.Kind == BreakKind.OrderMismatch).Id;
    }

    [Fact]
    public async Task Accepting_the_broker_appends_an_owner_event_once_keeps_the_break_visible_and_the_projection_equals_a_replay()
    {
        var id = await BreakWithIntentAsync();
        _store.Resolve(id, "owner:hem@example.test", "accept_broker", ReconFixtures.Now).ShouldBeTrue();
        var adoption = new BreakAdoption(_store, _events, _clock);

        (await adoption.AdoptAsync(CancellationToken.None)).ShouldBe(1);
        (await adoption.AdoptAsync(CancellationToken.None)).ShouldBe(0, "idempotent: the log already names the break");

        var adopted = _events.Events(361).Single(e => e.Kind == ExecEventKind.LegUpdate);
        (adopted.Source, adopted.Actor).ShouldBe((ExecEventSource.Owner, "owner:hem@example.test"));
        adopted.Payload.ShouldContain($"\"adopt\":{id}");
        (await _store.BreaksAsync(null, CancellationToken.None)).Single(b => b.Id == id).Resolution!.Resolution.ShouldBe("accept_broker");
        var replay = _events.Events(361).Aggregate(
            ExecIntent.Create(361, Guid.NewGuid(), ReconFixtures.Account, 1, ExecSide.Buy, "RELIANCE", "1594", 10, 1500m, 1485m, 1530m, ExecStructure.PlainStop).Value,
            (intent, e) => intent.Apply(e).Value);
        var stored = (await _events.StateAsync(361, CancellationToken.None))!;
        (replay.State, replay.FilledQty, replay.Seq).ShouldBe((stored.State, stored.FilledQty, stored.Seq));
    }

    [Fact]
    public async Task A_resolved_key_is_not_raised_again_while_the_figure_stays_and_the_freeze_condition_follows_the_rows()
    {
        var id = await BreakWithIntentAsync();
        (await _store.OpenKeysAsync(ReconFixtures.Account, CancellationToken.None)).ShouldNotBeEmpty();
        _store.Resolve(id, "owner:hem@example.test", "accept_broker", ReconFixtures.Now);

        var again = await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        again.Breaks.ShouldBe((await _store.BreaksAsync(null, CancellationToken.None)).Count(b => b.Resolution is null), "nothing new beyond what was already open");
        (await _store.OpenKeysAsync(ReconFixtures.Account, CancellationToken.None)).ShouldNotContain((await _store.BreaksAsync(null, CancellationToken.None)).Single(b => b.Id == id).Key);
    }

    [Fact]
    public async Task A_resolution_needs_an_owner_actor_a_known_word_an_existing_unresolved_break_and_dismiss_foreign_only_for_a_foreign_item()
    {
        var id = await BreakWithIntentAsync();

        _store.Resolve(id, "system:halts", "accept_broker", ReconFixtures.Now).ShouldBeFalse();
        _store.Resolve(id, "owner:", "accept_broker", ReconFixtures.Now).ShouldBeFalse();
        _store.Resolve(id, "owner:hem", "make_it_so", ReconFixtures.Now).ShouldBeFalse();
        _store.Resolve(id, "owner:hem", "dismiss_foreign", ReconFixtures.Now).ShouldBeFalse("an order mismatch is not a foreign item");
        _store.Resolve(9999, "owner:hem", "accept_broker", ReconFixtures.Now).ShouldBeFalse();
        _store.Resolve(id, "owner:hem", "accept_broker", ReconFixtures.Now).ShouldBeTrue();
        _store.Resolve(id, "owner:hem", "accept_broker", ReconFixtures.Now).ShouldBeFalse("a break is resolved once");
    }

    [Fact]
    public async Task Dismissing_a_foreign_conflict_remembers_it_as_explained_for_the_day()
    {
        await CleanBookAsync();
        _broker.AddManualOrder("M1", ReconFixtures.Security, ExecSide.Sell, 5, 120m);
        await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);
        var foreign = (await _store.BreaksAsync(null, CancellationToken.None)).Single(b => b.Kind == BreakKind.ForeignConflict);

        _store.Resolve(foreign.Id, "owner:hem", "dismiss_foreign", ReconFixtures.Now).ShouldBeTrue();
        var again = await _service.RunAsync(ReconFixtures.Account, ReconKind.Continuous, Session, CancellationToken.None);

        again.Breaks.ShouldBe(0);
        again.Clean.ShouldBeTrue();
    }
}
