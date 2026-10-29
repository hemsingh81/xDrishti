using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>Criteria 3, 4, 8b, 9, 10 and 12 of P12-04: the timeline, the ladder, the halt levels, the host that dies, the blind view and the fact P12-07 reads.</summary>
public sealed class ProtectionWatchdogTests
{
    [Fact]
    public async Task A_stop_live_at_4_seconds_gives_no_breach_and_nothing_is_sent()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.TickAsync(3);
        rig.IsOpen(AlarmCodes.ProtectionPending).ShouldBeTrue();
        await rig.StopAsync();
        var calls = rig.Broker.Calls;
        await rig.TickAsync(8);

        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(0);
        rig.IsOpen(AlarmCodes.ProtectionPending).ShouldBeFalse("cleared by the engine once the stop is live");
        rig.Broker.Calls.ShouldBe(calls, "no instruction is sent for a protected position");
        rig.Events.State(361).ShouldBe(ExecState.Protected);
        rig.Halts.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_stop_at_10_point_5_seconds_is_a_breach_with_step_A_and_the_repaired_stop_clears_it()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.TickAsync(10);
        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(0, "at 10 s the deadline has not passed");
        rig.OrdersOf(LegKind.Stop).ShouldBe(0);

        await rig.TickAsync(1);
        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(1);
        rig.Alarms.Raised.Single(a => a.Code == AlarmCodes.ProtectionBreach).Severity.ShouldBe(AlarmSeverity.Critical);
        var repair = rig.Broker.Legs.Single(l => l.Kind == LegKind.Stop);
        repair.ClientOrderId.ShouldBe(Rig.Cid(LegKind.Stop, 2), "step A is attempt S2");
        (repair.Qty, repair.Trigger, repair.Price, repair.Side).ShouldBe((10, 1485m, 1477.55m, ExecSide.Sell));
        rig.Halts.Requests.ShouldContain(r => r.Level == HaltLevel.Entries && r.TriggerCode == AlarmCodes.ProtectionBreach);

        await rig.TickAsync(2);
        rig.IsOpen(AlarmCodes.ProtectionBreach).ShouldBeFalse();
        rig.Count(AlarmCodes.StopReplaced).ShouldBe(1);
        rig.OrdersOf(LegKind.Stop).ShouldBe(1, "one repair only");
        rig.OrdersOf(LegKind.Flatten).ShouldBe(0);
    }

    [Fact]
    public async Task A_partial_fill_of_4_then_6_needs_cover_for_4_in_10_seconds_and_for_10_in_10_seconds_of_the_second()
    {
        var rig = new Rig();
        rig.Broker.MaxFillPerStep = 4;
        await rig.Broker.PlaceAsync(Rig.Entry(), CancellationToken.None);
        rig.Broker.SetPrice("1594", 1500m);
        rig.Source.RaisedAt = rig.Clock.GetUtcNow();
        await rig.TickAsync(3);
        await rig.StopAsync(4);
        await rig.TickAsync(2);
        rig.Source.NetOverride.ShouldBeNull();
        rig.Broker.MaxFillPerStep = 0;
        rig.Broker.SetPrice("1594", 1500m); // the other 6
        rig.Source.RaisedAt = rig.Clock.GetUtcNow();
        await rig.TickAsync(9);

        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(0, "9 s after the second fill is still inside its deadline, though 14 s after the first");
        await rig.StopAsync(6, attempt: 2);
        await rig.TickAsync(3);
        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(0);
        rig.IsOpen(AlarmCodes.ProtectionPending).ShouldBeFalse();
        rig.Events.State(361).ShouldBe(ExecState.Protected);
    }

    [Fact]
    public async Task A_partial_that_is_not_followed_in_time_is_a_breach_for_the_difference_only()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync(4);
        await rig.TickAsync(12);

        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(1);
        rig.Broker.Legs.Single(l => l.Kind == LegKind.Stop && l.ClientOrderId == Rig.Cid(LegKind.Stop, 2)).Qty.ShouldBe(6);
    }

    [Fact]
    public async Task A_stop_leg_silently_deleted_is_a_breach_and_step_A_places_it_again()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        await rig.TickAsync(2);
        rig.Broker.SilentlyDelete(Rig.Cid(LegKind.Stop, 1));
        await rig.TickAsync(14);

        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(1);
        rig.Count(AlarmCodes.StopReplaced).ShouldBe(1);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 2)).ShouldBe("PENDING");
        rig.OrdersOf(LegKind.Flatten).ShouldBe(0);
    }

    [Fact]
    public async Task A_rejecting_broker_fails_step_A_and_step_B_flattens_to_closed_with_fills_and_no_negative_position()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.TickAsync(10);
        rig.Broker.RejectNextWith = "rejected";
        await rig.TickAsync(1);
        rig.OrdersOf(LegKind.Stop).ShouldBe(0, "the repair was refused by the broker");

        await rig.TickAsync(1);
        rig.Events.State(361).ShouldBe(ExecState.Closing);
        var flatten = rig.Broker.Legs.Single(l => l.Kind == LegKind.Flatten);
        (flatten.Side, flatten.Qty, flatten.Price).ShouldBe((ExecSide.Sell, 10, 1487.50m), "reduce-only: a marketable limit at last less the collar-bounded step");
        rig.Broker.SetPrice("1594", 1495m);
        await rig.TickAsync(1);

        rig.Events.State(361).ShouldBe(ExecState.Closed);
        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(1);
        rig.Count(AlarmCodes.FlattenStarted).ShouldBe(1);
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(0);
        rig.Alarms.Open.ShouldBeEmpty("the engine cleared them when the position was flat");
    }

    [Fact]
    public async Task A_flatten_that_never_fills_is_three_re_prices_then_flatten_incomplete_a_halt_request_and_three_retries_then_the_owner()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        rig.Broker.RejectNextWith = null;
        await rig.TickAsync(10);
        rig.Broker.RejectNextWith = "rejected";
        await rig.TickAsync(120);

        rig.Count(AlarmCodes.FlattenIncomplete).ShouldBe(1);
        rig.Halts.Requests.ShouldContain(r => r.TriggerCode == AlarmCodes.FlattenIncomplete && r.Level == HaltLevel.Entries);
        var flattens = rig.Broker.Legs.Where(l => l.Kind == LegKind.Flatten).OrderBy(l => l.ClientOrderId).ToList();
        flattens.Count.ShouldBe(6, "three steps and F1 to F3, then the owner");
        flattens.Take(3).Select(l => l.Price).ShouldBe([1487.50m, 1480.05m, 1480.05m], "widening, capped at the 1% collar of the last price 1,495");
        flattens.ShouldAllBe(l => l.Qty == 10 && l.Side == ExecSide.Sell);
        rig.Broker.Legs.Count(l => l.Kind == LegKind.Flatten && rig.Broker.StatusOf(l.ClientOrderId) == "PENDING").ShouldBe(1, "never two flatten sells resting together");
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(10, "never negative: the position is untouched and the owner takes over");
    }

    [Fact]
    public async Task With_flatten_on_unprotected_false_the_alarms_and_the_halt_request_are_the_whole_response()
    {
        var rig = new Rig(ProtectionSettings.Default with { FlattenOnUnprotected = false });
        await rig.FilledAsync();
        rig.Broker.RejectNextWith = "rejected";
        await rig.TickAsync(40);

        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(1);
        rig.OrdersOf(LegKind.Flatten).ShouldBe(0);
        rig.Events.State(361).ShouldBe(ExecState.Filled);
        rig.Halts.Requests.ShouldContain(r => r.TriggerCode == AlarmCodes.ProtectionBreach);
    }

    [Fact]
    public async Task The_breach_is_written_as_an_alarm_row_and_an_alarm_event_on_the_intent()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.TickAsync(11);

        rig.Alarms.Open.ShouldContain(a => a.Code == AlarmCodes.ProtectionBreach && a.IntentId == 361 && a.AccountId == Rig.Account);
        rig.Events.Events(361).ShouldContain(e => e.Kind == ExecEventKind.Alarm && e.Payload.Contains("protection_breach", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_blind_view_is_protection_unknown_with_a_halt_request_and_never_a_flatten_or_a_send()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        var calls = rig.Broker.Calls;
        rig.Source.BookAge = TimeSpan.FromSeconds(31);
        await rig.TickAsync(60);

        rig.IsOpen(AlarmCodes.ProtectionUnknown).ShouldBeTrue();
        rig.Halts.Requests.ShouldContain(r => r.Level == HaltLevel.Entries && r.TriggerCode == AlarmCodes.ProtectionUnknown);
        rig.Broker.Calls.ShouldBe(calls);
        rig.OrdersOf(LegKind.Flatten).ShouldBe(0);
        rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(0);
    }

    [Fact]
    public async Task A_source_that_cannot_answer_or_settings_that_cannot_be_vouched_for_are_unknown_and_send_nothing()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        rig.Source.Unknown = true;
        await rig.TickAsync(20);
        rig.IsOpen(AlarmCodes.ProtectionUnknown).ShouldBeTrue();
        rig.Broker.Calls.ShouldBe(1, "only the test's own entry");

        var noSettings = new Rig();
        await noSettings.FilledAsync();
        noSettings.Settings = null!;
        await noSettings.TickAsync(20);
        noSettings.IsOpen(AlarmCodes.ProtectionUnknown).ShouldBeTrue();
        noSettings.Broker.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_host_dies_the_stop_fires_at_the_broker_and_on_restart_the_intent_closes_with_nothing_sent()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        await rig.TickAsync(2);
        var calls = rig.Broker.Calls;
        rig.Clock.Advance(TimeSpan.FromMinutes(20)); // xd-exec is down: no tick
        rig.Broker.SetPrice("1594", 1480m);
        rig.Broker.FilledOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe(10);
        var restarted = new ProtectionWatchdog(rig.Source, rig.Sender, rig.Broker, rig.Guard, rig.Report, () => rig.Settings, rig.Clock);
        await restarted.TickAsync(CancellationToken.None);

        rig.Events.State(361).ShouldBe(ExecState.Closed);
        rig.Broker.Calls.ShouldBe(calls);
        rig.Alarms.Open.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_negative_net_is_oversold_with_a_freeze_request_and_nothing_is_sent_to_correct_it()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.Broker.PlaceAsync(new BrokerLeg(Rig.Cid(LegKind.Exit, 1), LegKind.Exit, ExecSide.Sell, "1594", 20, 1400m), CancellationToken.None);
        rig.Broker.SetPrice("1594", 1500m);
        var calls = rig.Broker.Calls;
        await rig.TickAsync(30);

        rig.IsOpen(AlarmCodes.Oversold).ShouldBeTrue();
        rig.Alarms.Raised.Single(a => a.Code == AlarmCodes.Oversold).Severity.ShouldBe(AlarmSeverity.Critical);
        rig.Halts.Requests.ShouldContain(r => r.Level == HaltLevel.Freeze && r.TriggerCode == AlarmCodes.Oversold);
        rig.Broker.Calls.ShouldBe(calls);
        rig.Events.State(361).ShouldNotBe(ExecState.Reconciled);
        rig.Events.State(361).ShouldNotBe(ExecState.Closed, "a break is shown, not closed over");
    }

    [Fact]
    public async Task A_stop_in_transit_is_a_resend_when_repaired_and_the_gate_refuses_it_until_lookup_verified_is_on()
    {
        var transit = new BrokerOrderView("B9", Rig.Cid(LegKind.Stop, 1), "TRANSIT", "1594", ExecSide.Sell, 10, 0, 1477.55m, 1485m);

        var closed = new Rig();
        await closed.FilledAsync();
        closed.Source.BookOverride = [transit];
        var before = closed.Broker.Calls;
        await closed.TickAsync(15);
        closed.Broker.Calls.ShouldBe(before, "the resend gate keeps refusing");
        closed.IsOpen(AlarmCodes.SendUnknown).ShouldBeTrue();

        var open = new Rig(ProtectionSettings.Default with { LookupVerified = true });
        await open.FilledAsync();
        open.Source.BookOverride = [transit];
        var calls = open.Broker.Calls;
        await open.TickAsync(15);
        open.Broker.Calls.ShouldBeGreaterThan(calls, "negative control: with the owner's setting on, the repair is attempted");
    }

    [Fact]
    public async Task A_closed_account_gate_or_the_live_environment_sends_nothing()
    {
        foreach (var rig in new[] { new Rig(mode: new ModeAnswer(ExecMode.SemiAuto, false)), new Rig(environment: ExecEnvironment.Live), new Rig(environment: ExecEnvironment.None) })
        {
            await rig.FilledAsync();
            var calls = rig.Broker.Calls;
            await rig.TickAsync(40);
            rig.Broker.Calls.ShouldBe(calls);
            rig.Count(AlarmCodes.ProtectionBreach).ShouldBe(1, "the alarm still stands");
        }
    }

    [Fact]
    public async Task A_lowered_mode_does_not_stop_the_protection_of_an_open_position_but_the_global_switch_does()
    {
        var paper = new Rig(mode: new ModeAnswer(ExecMode.Paper, true));
        await paper.FilledAsync();
        await paper.TickAsync(14);
        paper.OrdersOf(LegKind.Stop).ShouldBe(1, "authority covers the intent's whole protective life");
    }

    public static TheoryData<HaltLevel, bool> Halts() => new() { { HaltLevel.Entries, true }, { HaltLevel.Cancel, true }, { HaltLevel.ExitAll, true } };

    [Theory]
    [MemberData(nameof(Halts))]
    public async Task At_entries_cancel_and_exit_all_a_stop_repair_is_sent(HaltLevel level, bool sent)
    {
        var rig = new Rig();
        rig.Source.Halt = level;
        await rig.FilledAsync();
        await rig.TickAsync(14);

        (rig.OrdersOf(LegKind.Stop) == 1).ShouldBe(sent);
    }

    [Fact]
    public async Task Under_freeze_a_repair_and_a_flatten_need_a_fresh_broker_position_read_that_agrees()
    {
        var agrees = new Rig();
        agrees.Source.Halt = HaltLevel.Freeze;
        await agrees.FilledAsync();
        agrees.Source.BrokerNet = 10;
        agrees.Source.BrokerPositionAt = agrees.Clock.GetUtcNow();
        for (var i = 0; i < 14; i++)
        {
            agrees.Source.BrokerPositionAt = agrees.Clock.GetUtcNow();
            await agrees.TickAsync();
        }

        agrees.OrdersOf(LegKind.Stop).ShouldBe(1);

        var below = new Rig();
        below.Source.Halt = HaltLevel.Freeze;
        await below.FilledAsync();
        below.Source.BrokerNet = 6;
        for (var i = 0; i < 40; i++)
        {
            below.Source.BrokerPositionAt = below.Clock.GetUtcNow();
            await below.TickAsync();
        }

        below.OrdersOf(LegKind.Stop).ShouldBe(0);
        below.OrdersOf(LegKind.Flatten).ShouldBe(0);
        below.IsOpen(AlarmCodes.ProtectionUnknown).ShouldBeTrue();

        var stale = new Rig();
        stale.Source.Halt = HaltLevel.Freeze;
        await stale.FilledAsync();
        stale.Source.BrokerNet = 10;
        stale.Source.BrokerPositionAt = stale.Clock.GetUtcNow() - TimeSpan.FromSeconds(16);
        await stale.TickAsync(40);
        stale.OrdersOf(LegKind.Stop).ShouldBe(0, "a read 16 s old no longer counts");
        stale.IsOpen(AlarmCodes.ProtectionUnknown).ShouldBeTrue();
    }

    [Fact]
    public async Task A_protection_breach_halts_new_entries_and_an_unreadable_halt_source_is_not_involved()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.TickAsync(12);

        rig.Halts.Requests.Count(r => r.TriggerCode == AlarmCodes.ProtectionBreach).ShouldBe(1, "one request per new alarm, not one per tick");
    }
}
