using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>Criteria 1, 2, 5 (the rules), 6 (the arithmetic) and the square-off order of P12-04, on hand rows.</summary>
public sealed class ProtectionRulesTests
{
    private static ExecIntent Intent(ExecStructure structure, ExecSide side = ExecSide.Buy) =>
        ExecIntent.Create(361, Guid.NewGuid(), Guid.NewGuid(), 1, side, "RELIANCE", "1594", 10, 1500m, 1485m, 1530m, structure).Value;

    [Fact]
    public void Super_gives_the_entry_with_the_stop_and_the_target_and_plain_stop_gives_the_entry_alone()
    {
        ProtectionPlan.Entry(Intent(ExecStructure.Super)).ShouldBe(new EntryInstruction(ExecStructure.Super, 10, 1500m, 1485m, 1530m));
        ProtectionPlan.Entry(Intent(ExecStructure.PlainStop)).ShouldBe(new EntryInstruction(ExecStructure.PlainStop, 10, 1500m, null, null));
    }

    [Fact]
    public void A_fill_of_4_gives_a_sell_stop_for_4_with_the_trigger_and_the_limit_rounded_down_to_the_tick()
    {
        ProtectionPlan.StopFor(4, 1485m, 0.5m).ShouldBe(new StopInstruction(4, 1485.00m, 1477.55m));
        ProtectionPlan.StopFor(0, 1485m, 0.5m).ShouldBeNull();
    }

    [Fact]
    public void There_is_no_sell_entry()
    {
        ProtectionPlan.Entry(Intent(ExecStructure.Super, ExecSide.Sell)).ShouldBeNull();
        ProtectionPlan.Entry(Intent(ExecStructure.PlainStop, ExecSide.Sell)).ShouldBeNull();
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 4, 0, 30, TimeSpan.Zero);
    private static readonly TimeSpan Ten = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Blind = TimeSpan.FromSeconds(30);

    private static StopLegView Leg(int qty, string status = "PENDING", decimal trigger = 1485m) => new("L", qty, status, trigger);

    private static ProtectionVerdict Judge(int net, StopLegView[] legs, int sinceAgo = 0, int readAgo = 1, decimal intentStop = 1485m, bool noRead = false) =>
        ProtectionMonitor.Judge(new ProtectionInput(net, legs, intentStop, sinceAgo == 0 ? null : Now.AddSeconds(-sinceAgo), Now, noRead ? null : Now.AddSeconds(-readAgo), Ten, Blind));

    public static TheoryData<string, int, int[], string[], int, ProtectionVerdict> Rows()
    {
        var d = new TheoryData<string, int, int[], string[], int, ProtectionVerdict>();
        void Add(string name, int net, int[] qty, string[] status, int sinceAgo, ProtectionVerdict expected) => d.Add(name, net, qty, status, sinceAgo, expected);
        Add("flat, no stops", 0, [], [], 0, new ProtectionVerdict.Flat());
        Add("flat, a stale stop still open", 0, [10], ["PENDING"], 0, new ProtectionVerdict.Flat());
        Add("covered exactly", 10, [10], ["PENDING"], 0, new ProtectionVerdict.Protected());
        Add("covered by two legs", 10, [6, 4], ["PENDING", "PENDING"], 0, new ProtectionVerdict.Protected());
        Add("covered with more than needed", 10, [12], ["PENDING"], 0, new ProtectionVerdict.Protected());
        Add("no stop, just filled", 10, [], [], 0, new ProtectionVerdict.Pending(Now + Ten, 10));
        Add("no stop, 4 s", 10, [], [], 4, new ProtectionVerdict.Pending(Now.AddSeconds(-4) + Ten, 10));
        Add("no stop, exactly at the deadline", 10, [], [], 10, new ProtectionVerdict.Pending(Now, 10));
        Add("no stop, past the deadline", 10, [], [], 11, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("transit stop does not protect (inside)", 10, [10], ["TRANSIT"], 2, new ProtectionVerdict.Pending(Now.AddSeconds(-2) + Ten, 10));
        Add("transit stop does not protect (past)", 10, [10], ["TRANSIT"], 12, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("rejected stop", 10, [10], ["REJECTED"], 12, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("cancelled stop", 10, [10], ["CANCELLED"], 12, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("expired stop", 10, [10], ["EXPIRED"], 12, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("traded stop (gone)", 10, [10], ["TRADED"], 12, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("part-traded stop does not count", 10, [10], ["PART_TRADED"], 12, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("stop smaller than q, inside", 10, [6], ["PENDING"], 3, new ProtectionVerdict.Pending(Now.AddSeconds(-3) + Ten, 4));
        Add("stop smaller than q, past", 10, [6], ["PENDING"], 20, new ProtectionVerdict.Breach("protection_breach", 4));
        Add("stop of 0 remaining", 10, [0], ["PENDING"], 20, new ProtectionVerdict.Breach("protection_breach", 10));
        Add("one pending one transit", 10, [6, 4], ["PENDING", "TRANSIT"], 20, new ProtectionVerdict.Breach("protection_breach", 4));
        Add("negative net", -3, [], [], 0, new ProtectionVerdict.Breach("oversold", 0));
        Add("negative net with a stop", -3, [3], ["PENDING"], 0, new ProtectionVerdict.Breach("oversold", 0));
        Add("partial 4 covered", 4, [4], ["PENDING"], 0, new ProtectionVerdict.Protected());
        Add("partial 4 then 10, stop still 4", 10, [4], ["PENDING"], 2, new ProtectionVerdict.Pending(Now.AddSeconds(-2) + Ten, 6));
        Add("partial 4 then 10, stop followed", 10, [4, 6], ["PENDING", "PENDING"], 2, new ProtectionVerdict.Protected());
        return d;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_monitor_table(string name, int net, int[] qty, string[] status, int sinceAgo, ProtectionVerdict expected)
    {
        var legs = qty.Select((q, i) => Leg(q, status[i])).ToArray();

        Judge(net, legs, sinceAgo).ShouldBe(expected, name);
    }

    [Fact]
    public void A_stop_looser_than_the_intents_is_a_mismatch_even_when_the_quantity_is_covered()
    {
        Judge(10, [Leg(10, trigger: 1480m)]).ShouldBe(new ProtectionVerdict.Breach("stop_mismatch", 10));
        Judge(10, [Leg(10, trigger: 1485m)]).ShouldBe(new ProtectionVerdict.Protected());
        Judge(10, [Leg(10, trigger: 1490m)]).ShouldBe(new ProtectionVerdict.Protected(), "a tighter stop is fine");
        Judge(10, [Leg(4, trigger: 1480m), Leg(6, trigger: 1485m)]).ShouldBe(new ProtectionVerdict.Breach("stop_mismatch", 4));
    }

    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(false, 29, 0)]
    [InlineData(false, 31, 31)]
    [InlineData(false, 3600, 3600)]
    public void A_missing_or_old_broker_view_is_unknown_and_never_protected(bool noRead, int readAgo, int _)
    {
        var verdict = Judge(10, [Leg(10)], readAgo: readAgo, noRead: noRead);

        if (noRead || readAgo > 30)
        {
            verdict.ShouldBeOfType<ProtectionVerdict.Unknown>();
        }
        else
        {
            verdict.ShouldBeOfType<ProtectionVerdict.Protected>();
        }
    }

    [Fact]
    public void The_hand_fixture_breakeven_at_1R_moves_the_stop_to_1500_05_only_at_1515()
    {
        StopRules.BreakevenTrigger(1500m, 1485m, 1515m, 1m).ShouldBe(1500.05m);
        StopRules.BreakevenTrigger(1500m, 1485m, 1514.95m, 1m).ShouldBeNull();
        StopRules.BreakevenTrigger(1500m, 1485m, 1600m, 0m).ShouldBeNull("off by default");
        StopRules.BreakevenTrigger(1500m, 1500m, 1600m, 1m).ShouldBeNull("no risk to measure");
    }

    [Theory]
    [InlineData(1485, 1490, 1495, 0, null)]
    [InlineData(1485, 1484.95, 1495, 0, "stop_loosen")]
    [InlineData(1485, 1485, 1495, 0, "no_change")]
    [InlineData(1485, 1494.95, 1495, 0, "too_close_to_last")]
    [InlineData(1485, 1494.9, 1495, 0, null)]
    [InlineData(1485, 1490, 1495, 20, "modification_cap")]
    [InlineData(1485, 1490.02, 1495, 0, "price_or_tick")]
    [InlineData(1485, 1490, 0, 0, "no_last_price")]
    public void Tighten_only_two_ticks_from_last_and_the_cap(double current, double next, double last, int mods, string? expected)
    {
        StopRules.CheckTighten((decimal)current, (decimal)next, last == 0 ? null : (decimal)last, mods, 20).ShouldBe(expected);
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 4, 6)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 12, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(-3, 0, 0)]
    [InlineData(10, -5, 10)]
    public void An_exit_never_exceeds_the_position_less_the_exits_in_flight(int net, int inFlight, int expected) => StopRules.MaxExitQty(net, inFlight).ShouldBe(expected);

    [Fact]
    public void The_flatten_walk_widens_by_a_step_and_never_leaves_the_exit_collar()
    {
        StopRules.FlattenPrice(1500m, 1.0m, 0.5m, 0).ShouldBe(1492.50m);
        StopRules.FlattenPrice(1500m, 1.0m, 0.5m, 1).ShouldBe(1485.00m);
        StopRules.FlattenPrice(1500m, 1.0m, 0.5m, 2).ShouldBe(1485.00m, "capped at the collar");
        StopRules.FlattenPrice(1500m, 1.0m, 0.5m, 9).ShouldBe(1485.00m);
    }

    private static SquareOffState Sq(string time, int working = 0, bool tried = false, bool target = false, int net = 0, int inFlight = 0, bool stop = false) =>
        new(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), new TimeOnly(15, 0), new TimeOnly(15, 15), working, tried, target, false, net, inFlight, stop);

    [Theory]
    [InlineData("14:59", 1, false, false, 0, 0, false, SquareOffStepKind.None)]
    [InlineData("15:00", 1, false, false, 0, 0, false, SquareOffStepKind.CancelEntries)]
    [InlineData("15:16", 1, false, false, 10, 0, true, SquareOffStepKind.CancelEntries)]
    [InlineData("15:16", 1, true, false, 10, 0, true, SquareOffStepKind.ExitFailed)]
    [InlineData("15:00", 1, true, false, 0, 0, false, SquareOffStepKind.CancelEntries)]
    [InlineData("15:14", 0, false, true, 10, 0, true, SquareOffStepKind.None)]
    [InlineData("15:15", 0, false, true, 10, 0, true, SquareOffStepKind.CancelTarget)]
    [InlineData("15:15", 0, false, false, 10, 0, true, SquareOffStepKind.SendExit)]
    [InlineData("15:15", 0, false, false, 10, 10, true, SquareOffStepKind.None)]
    [InlineData("15:15", 0, false, false, 10, 4, true, SquareOffStepKind.SendExit)]
    [InlineData("15:15", 0, false, false, 0, 0, true, SquareOffStepKind.CancelStop)]
    [InlineData("15:15", 0, false, false, 0, 0, false, SquareOffStepKind.Done)]
    public void The_square_off_order(string time, int working, bool tried, bool target, int net, int inFlight, bool stop, SquareOffStepKind expected) =>
        SquareOffPlan.Next(Sq(time, working, tried, target, net, inFlight, stop)).Kind.ShouldBe(expected);

    [Fact]
    public void The_exit_quantity_is_the_net_less_what_is_in_flight() =>
        SquareOffPlan.Next(Sq("15:15", net: 10, inFlight: 4, stop: true)).ShouldBe(new SquareOffStep(SquareOffStepKind.SendExit, 6));

    [Fact]
    public void The_settings_are_the_registry_defaults_and_an_out_of_range_value_makes_them_unavailable()
    {
        var s = ProtectionSettings.Default;

        s.Deadline.ShouldBe(TimeSpan.FromSeconds(10));
        s.StopLimitBufferPct.ShouldBe(0.5m);
        s.FlattenOnUnprotected.ShouldBeTrue();
        s.FlattenWalkSteps.ShouldBe(3);
        s.BreakevenAtR.ShouldBe(0m);
        s.StopUnfilled.ShouldBe(TimeSpan.FromSeconds(5));
        s.BlindAfter.ShouldBe(TimeSpan.FromSeconds(30));
        s.LookupVerified.ShouldBeFalse();
        ProtectionSettings.FromValues(new Dictionary<string, string> { ["exec.flatten_walk_steps"] = "9" }).ShouldBeNull();
        ProtectionSettings.FromValues(new Dictionary<string, string> { ["exec.stop_limit_buffer_pct"] = "x" }).ShouldBeNull();
        ProtectionSettings.FromValues(new Dictionary<string, string> { ["exec.flatten_on_unprotected"] = "false" })!.FlattenOnUnprotected.ShouldBeFalse();
    }

    [Fact]
    public void The_twelve_alarm_codes_have_a_severity_and_the_protection_ones_are_critical()
    {
        AlarmCodes.All.Count.ShouldBe(12);
        AlarmCodes.All[AlarmCodes.ProtectionBreach].ShouldBe(AlarmSeverity.Critical);
        AlarmCodes.All[AlarmCodes.Oversold].ShouldBe(AlarmSeverity.Critical);
        AlarmCodes.All[AlarmCodes.ProtectionPending].ShouldBe(AlarmSeverity.Warn);
        AlarmCodes.All[AlarmCodes.BrokerSquareoff].ShouldBe(AlarmSeverity.Info);
    }

    [Fact]
    public void Loosening_and_adding_stay_refused_in_every_mode()
    {
        foreach (var mode in Enum.GetValues<ExecMode>())
        {
            ActionPolicy.Check(ExecAction.StopLoosen, mode).ShouldBe(ActionVerdict.Refused);
            ActionPolicy.Check(ExecAction.AddToPosition, mode).ShouldBe(ActionVerdict.Refused);
            ActionPolicy.Check(ExecAction.ReEntry, mode).ShouldBe(ActionVerdict.Refused);
        }
    }
}
