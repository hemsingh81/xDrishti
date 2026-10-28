using XDrishti.Domain.Accounts;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-04b: the evidence gates G1 to G7, the breach rules B1 to B7, the loss measure and the halts, by hand-worked tables.</summary>
public sealed class RampGatesTests
{
    private static readonly RampThresholds T = RampThresholds.Defaults;

    // n values with the exact mean and sample sd: half at mean + d, half at mean - d, d = sd * sqrt((n - 1) / n).
    private static List<double> Sample(int n, double mean, double sd)
    {
        var d = sd * Math.Sqrt((n - 1.0) / n);
        return [.. Enumerable.Range(0, n).Select(i => i % 2 == 0 ? mean + d : mean - d)];
    }

    private static RampGate Slip(IReadOnlyList<double> values) =>
        RampGates.Evaluate(new RampEvidence { SlippageR = values }, 1, T).Single(g => g.Code == RampGate.Slippage);

    [Fact]
    public void G3_passes_when_the_mean_and_the_upper_bound_are_within_the_limits()
    {
        var gate = Slip(Sample(20, 0.02, 0.12)); // upper = 0.02 + 1.729 x 0.12 / sqrt(20) = 0.0664
        gate.State.ShouldBe(GateStates.Pass);
        gate.Number.ShouldNotBeNull().ShouldBe(0.02, 1e-9);
        gate.N.ShouldBe(20);
    }

    [Fact]
    public void G3_fails_on_a_mean_above_the_gap()
    {
        Slip(Sample(20, 0.06, 0.12)).State.ShouldBe(GateStates.Fail);
    }

    [Fact]
    public void G3_is_insufficient_when_only_the_upper_bound_is_too_high()
    {
        var gate = Slip(Sample(20, 0.04, 0.20)); // upper = 0.04 + 1.729 x 0.20 / sqrt(20) = 0.1173
        gate.State.ShouldBe(GateStates.Insufficient);
        gate.Reason.ShouldContain("not demonstrated");
    }

    [Fact]
    public void G3_is_insufficient_below_twenty_entries_and_never_a_pass()
    {
        Slip(Sample(19, 0.0, 0.01)).State.ShouldBe(GateStates.Insufficient);
        Slip([]).Passes.ShouldBeFalse();
    }

    [Theory]
    [InlineData(9, 1.833)]
    [InlineData(19, 1.729)]
    [InlineData(25, 1.711)] // row 24 (true 1.708), not the higher row 29
    [InlineData(10, 1.833)] // the row at or below, never the next higher (true 1.812)
    [InlineData(15, 1.761)]
    [InlineData(60, 1.671)]
    [InlineData(100, 1.671)]
    [InlineData(120, 1.645)]
    public void The_one_sided_quantile_follows_the_spec_table(int df, double expected) => OneSidedT.Quantile95(df).ShouldBe(expected);

    [Fact]
    public void G1_needs_twenty_sessions_since_the_step()
    {
        RampGates.Evaluate(new RampEvidence { Sessions = 19 }, 1, T)[0].State.ShouldBe(GateStates.Insufficient);
        RampGates.Evaluate(new RampEvidence { Sessions = 20 }, 1, T)[0].State.ShouldBe(GateStates.Pass);
    }

    [Fact]
    public void G2_counts_graded_round_trips_and_limits_the_uncertain_ones()
    {
        var graded = Enumerable.Range(0, 20).Select(_ => new TripFact(0.5, false, null)).ToList();
        RampGates.Evaluate(new RampEvidence { Trips = graded }, 1, T)[1].State.ShouldBe(GateStates.Pass);
        RampGates.Evaluate(new RampEvidence { Trips = graded }, 2, T)[1].State.ShouldBe(GateStates.Insufficient); // S2 needs 40
        var mixed = graded.Concat(Enumerable.Range(0, 3).Select(_ => new TripFact(null, true, null))).ToList(); // 3 of 23 = 13 %
        RampGates.Evaluate(new RampEvidence { Trips = mixed }, 1, T)[1].State.ShouldBe(GateStates.Fail);
    }

    private static RampEvidence Disciplined() => new()
    {
        Trips = [.. Enumerable.Range(0, 20).Select(_ => new TripFact(0.2, false, null))],
        Entries = 20,
        IntradayEntries = 10,
        ProtectedIntraday = 10,
    };

    [Fact]
    public void G4_passes_a_disciplined_record_and_names_each_failure()
    {
        RampGates.Evaluate(Disciplined(), 1, T)[3].State.ShouldBe(GateStates.Pass);
        RampGates.Evaluate(Disciplined() with { DiscretionaryTrades = 3 }, 1, T)[3].State.ShouldBe(GateStates.Fail); // 3 of 23 = 13 %
        RampGates.Evaluate(Disciplined() with { OverAllocations = 1 }, 1, T)[3].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(Disciplined() with { ChasedEntries = 5 }, 1, T)[3].State.ShouldBe(GateStates.Fail); // 25 %
        RampGates.Evaluate(Disciplined() with { ProtectedIntraday = 8 }, 1, T)[3].State.ShouldBe(GateStates.Fail); // 80 %
    }

    [Fact]
    public void G4_is_unavailable_without_order_evidence_and_never_a_pass()
    {
        var gate = RampGates.Evaluate(Disciplined() with { ProtectionUnavailable = 2 }, 1, T)[3];
        gate.State.ShouldBe(GateStates.Unavailable);
        gate.Passes.ShouldBeFalse();
    }

    [Fact]
    public void G5_checks_the_drawdown_the_halt_fills_and_the_daily_halts()
    {
        var trips = new[] { new TripFact(1, false, null) };
        RampGates.Evaluate(new RampEvidence { Trips = trips, DrawdownR = 4.9 }, 1, T)[4].State.ShouldBe(GateStates.Pass);
        RampGates.Evaluate(new RampEvidence { Trips = trips, DrawdownR = 5.1 }, 1, T)[4].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(new RampEvidence { Trips = trips, DrawdownR = 1, AfterHaltFills = 1 }, 1, T)[4].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(new RampEvidence { Trips = trips, DrawdownR = 1, DailyHalts = 2 }, 1, T)[4].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(new RampEvidence(), 1, T)[4].Passes.ShouldBeFalse(); // nothing closed: not a pass
    }

    [Fact]
    public void G6_needs_a_healthy_share_no_recent_gap_and_no_history_disagreement()
    {
        RampGates.Evaluate(new RampEvidence { CaptureSessions = 20, HealthySessions = 19 }, 1, T)[5].State.ShouldBe(GateStates.Pass); // 95 %
        RampGates.Evaluate(new RampEvidence { CaptureSessions = 20, HealthySessions = 18 }, 1, T)[5].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(new RampEvidence { CaptureSessions = 20, HealthySessions = 20, GapLast5 = 1 }, 1, T)[5].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(new RampEvidence { CaptureSessions = 20, HealthySessions = 20, HistoryDisagreementsLast10 = 1 }, 1, T)[5].State.ShouldBe(GateStates.Fail);
        RampGates.Evaluate(new RampEvidence(), 1, T)[5].State.ShouldBe(GateStates.Unavailable);
    }

    [Fact]
    public void G7_is_information_and_warns_above_a_third_of_one_r_but_never_blocks()
    {
        var gates = RampGates.Evaluate(new RampEvidence { Trips = [new TripFact(1, false, 0.5)] }, 1, T);
        gates[6].State.ShouldBe(GateStates.Info);
        gates[6].Reason.ShouldContain("too small for the fixed costs");
        RampGates.Evaluate(new RampEvidence(), 1, T)[6].State.ShouldBe(GateStates.Unavailable);
        RampGates.Eligible(gates).ShouldBeFalse();
    }

    [Fact]
    public void The_step_is_eligible_only_when_g1_to_g6_all_pass()
    {
        var all = Disciplined() with
        {
            Sessions = 20,
            SlippageR = Sample(20, 0.02, 0.12),
            DrawdownR = 2,
            CaptureSessions = 20,
            HealthySessions = 20,
        };
        RampGates.Eligible(RampGates.Evaluate(all, 1, T)).ShouldBeTrue();
        RampGates.Eligible(RampGates.Evaluate(all with { Sessions = 19 }, 1, T)).ShouldBeFalse();
        RampGates.Eligible(RampGates.Evaluate(all with { ProtectionUnavailable = 1 }, 1, T)).ShouldBeFalse();
    }

    [Fact]
    public void The_gate_snapshot_hash_is_stable_and_changes_with_the_text()
    {
        var a = GateSnapshot.From(1, RampGates.Evaluate(new RampEvidence { Sessions = 3 }, 1, T));
        var b = GateSnapshot.From(1, RampGates.Evaluate(new RampEvidence { Sessions = 3 }, 1, T));
        var c = GateSnapshot.From(1, RampGates.Evaluate(new RampEvidence { Sessions = 4 }, 1, T));
        a.Hash.ShouldBe(b.Hash);
        a.Hash.ShouldNotBe(c.Hash);
        a.Hash.Length.ShouldBe(64);
    }

    // ---- breach rules ----
    [Fact]
    public void B1_holds_on_one_fill_after_a_halt_and_steps_down_on_a_second()
    {
        BreachRules.Evaluate(new RampEvidence { AfterHaltLast10 = 1 }, T, 5).ShouldHaveSingleItem().ShouldBeEquivalentTo(new Breach("B1", Breach.Hold, "an entry was filled after a halt was open"));
        BreachRules.Evaluate(new RampEvidence { AfterHaltLast10 = 2 }, T, 5).ShouldHaveSingleItem().Action.ShouldBe(Breach.StepDown);
        BreachRules.Evaluate(new RampEvidence(), T, 5).ShouldBeEmpty();
    }

    [Fact]
    public void B2_and_B3_hold_on_two_events_in_ten_sessions()
    {
        BreachRules.Evaluate(new RampEvidence { OverAllocationLast10 = 1 }, T, 5).ShouldBeEmpty();
        BreachRules.Evaluate(new RampEvidence { OverAllocationLast10 = 2 }, T, 5).ShouldHaveSingleItem().Code.ShouldBe("B2");
        BreachRules.Evaluate(new RampEvidence { UnprotectedLast10 = 2 }, T, 5).ShouldHaveSingleItem().Code.ShouldBe("B3");
    }

    [Fact]
    public void B4_holds_only_when_the_lower_bound_is_above_the_limit_with_fifteen_entries()
    {
        BreachRules.Evaluate(new RampEvidence { SlippageR = Sample(15, 0.30, 0.10) }, T, 5).ShouldContain(b => b.Code == "B4"); // lower = 0.30 - 1.761 x 0.10 / sqrt(15) = 0.2545
        BreachRules.Evaluate(new RampEvidence { SlippageR = Sample(14, 0.30, 0.10) }, T, 5).ShouldBeEmpty();
        BreachRules.Evaluate(new RampEvidence { SlippageR = Sample(15, 0.10, 0.20) }, T, 5).ShouldBeEmpty(); // lower bound below the limit
    }

    [Fact]
    public void B5_holds_on_two_bad_capture_sessions_of_the_last_five()
    {
        BreachRules.Evaluate(new RampEvidence { BadCaptureLast5 = 1 }, T, 5).ShouldBeEmpty();
        BreachRules.Evaluate(new RampEvidence { BadCaptureLast5 = 2 }, T, 5).ShouldHaveSingleItem().Code.ShouldBe("B5");
    }

    [Fact]
    public void B6_and_B7_step_down_on_a_drawdown_above_the_weekly_limit_and_a_second_weekly_halt()
    {
        BreachRules.Evaluate(new RampEvidence { DrawdownR = 5 }, T, 5).ShouldBeEmpty();
        BreachRules.Evaluate(new RampEvidence { DrawdownR = 5.01 }, T, 5).ShouldHaveSingleItem().ShouldBeEquivalentTo(new Breach("B6", Breach.StepDown, "drawdown 5.01 R at this step is above the weekly limit of 5 R"));
        BreachRules.Evaluate(new RampEvidence { WeeklyHaltsLast8Weeks = 1 }, T, 5).ShouldBeEmpty();
        BreachRules.Evaluate(new RampEvidence { WeeklyHaltsLast8Weeks = 2 }, T, 5).ShouldHaveSingleItem().Code.ShouldBe("B7");
    }

    [Fact]
    public void A_hold_is_cleared_only_after_the_cooldown_without_a_new_event()
    {
        BreachRules.HoldConditions(new RampEvidence { AfterHaltLast10 = 1, AfterHaltLast5 = 0 }, T).ShouldBeEmpty(); // the event is older than five sessions
        BreachRules.HoldConditions(new RampEvidence { AfterHaltLast10 = 1, AfterHaltLast5 = 1 }, T).ShouldHaveSingleItem().Code.ShouldBe("B1");
        BreachRules.HoldConditions(new RampEvidence { BadCaptureLast5 = 2 }, T).ShouldHaveSingleItem().Code.ShouldBe("B5"); // a condition, not an event
        BreachRules.HoldConditions(new RampEvidence { DrawdownR = 9 }, T).ShouldBeEmpty(); // a step-down rule is lifted by a new step, not a clear
    }

    // ---- loss measure and halts ----
    private static LiveFill Fill(string symbol, Side side, long qty, decimal price, int minute, string product = "INTRADAY") =>
        new(Guid.NewGuid(), Guid.Empty, "T" + minute + symbol, null, symbol, side, product, qty, price, new DateOnly(2026, 11, 3), new TimeOnly(10, minute));

    private static IReadOnlyList<LiveFill> Day() =>
        [Fill("AAA", Side.Buy, 100, 100m, 1), Fill("AAA", Side.Sell, 100, 98.7m, 20), Fill("BBB", Side.Buy, 100, 50m, 5)];

    [Fact]
    public void The_loss_is_realised_plus_the_open_lots_marked()
    {
        var day = DayLoss.Measure(Day(), s => s == "BBB" ? 48.6m : null); // realised -130, open -140
        day.State.ShouldBe(MeasureStates.Value);
        day.Realised.ShouldBe(-130m);
        day.Open.ShouldBe(-140m);
        day.Loss.ShouldBe(270m);
    }

    [Fact]
    public void Minus_270_at_one_r_of_125_is_2_16_r_and_opens_a_daily_halt_but_minus_240_does_not()
    {
        var oneR = HaltRules.OneR(100_000m, 0.125).ShouldNotBeNull();
        oneR.ShouldBe(125m);
        var halt = HaltRules.Daily(DayLoss.Measure(Day(), s => s == "BBB" ? 48.6m : null), oneR, 100_000m, 2m, 1.5m).ShouldNotBeNull();
        halt.LossR.ShouldBe(2.16, 1e-9);
        halt.Action.ShouldBe(HaltCodes.StopNew);
        HaltRules.Daily(DayLoss.Measure(Day(), s => s == "BBB" ? 48.9m : null), oneR, 100_000m, 2m, 1.5m).ShouldBeNull(); // -130 - 110 = -240 = 1.92 R
    }

    [Fact]
    public void An_open_lot_without_a_price_makes_the_measure_realised_only_and_it_says_so()
    {
        var day = DayLoss.Measure(Day(), _ => null);
        day.State.ShouldBe(MeasureStates.RealisedOnly);
        day.Open.ShouldBeNull();
        day.OpenUnmarked.ShouldBe(1);
        day.Loss.ShouldBe(130m);
    }

    [Fact]
    public void An_unavailable_day_has_no_loss_figure_and_never_opens_a_halt()
    {
        DayLoss.Unavailable.Loss.ShouldBeNull();
        DayLoss.Unavailable.Net.ShouldBeNull();
        HaltRules.Daily(DayLoss.Unavailable, 125m, 100_000m, 2m, 1.5m).ShouldBeNull();
    }

    [Fact]
    public void A_delivery_sale_with_nothing_bought_today_is_a_carried_position_and_is_not_measured()
    {
        var day = DayLoss.Measure([Fill("CCC", Side.Sell, 40, 1262m, 5, "CNC")], _ => null);
        day.CarriedQty.ShouldBe(40);
        day.Loss.ShouldBe(0m);
    }

    [Fact]
    public void The_percentage_limit_can_open_a_halt_before_the_r_limit()
    {
        // 1 R = 5,000 (a large risk), loss 1,600 = 0.32 R but 1.6 % of 100,000
        var loss = new DayLoss(MeasureStates.Value, -1600m, 0m, 0, 0);
        HaltRules.Daily(loss, 5000m, 100_000m, 2m, 1.5m).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(WeeklyLossAction.HalveSize, HaltCodes.Halve)]
    [InlineData(WeeklyLossAction.StopNewEntries, HaltCodes.StopNew)]
    [InlineData(WeeklyLossAction.AlertOnly, HaltCodes.AlertOnly)]
    public void A_weekly_halt_takes_the_action_of_the_profile(WeeklyLossAction action, string expected)
    {
        HaltRules.Weekly(MeasureStates.Value, 700m, 125m, 100_000m, 5m, action).ShouldNotBeNull().Action.ShouldBe(expected); // 5.6 R
        HaltRules.Weekly(MeasureStates.Value, 600m, 125m, 100_000m, 5m, action).ShouldBeNull(); // 4.8 R
    }

    // ---- ramp events ----
    private static readonly Guid Account = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a01");

    private static GateSnapshot Eligible()
    {
        var gates = new[] { RampGate.Sessions, RampGate.Volume, RampGate.Slippage, RampGate.Discipline, RampGate.Containment, RampGate.Capture }.Select(c => new RampGate(c, GateStates.Pass, 1, 1, 20, "ok")).Append(new RampGate(RampGate.CostDrag, GateStates.Info, null, null, null, "x")).ToList();
        return GateSnapshot.From(1, gates);
    }

    [Fact]
    public void A_raise_by_one_step_needs_every_gate_and_carries_the_frozen_text()
    {
        var current = new RampState(1, 1, false);
        var blocked = RampEvent.OwnerSetStep(Account, current, 2, "owner:hem", "ok", GateSnapshot.From(1, RampGates.Evaluate(new RampEvidence(), 1, T)));
        blocked.Error.Code.ShouldBe(RampErrors.GatesNotMetCode);
        blocked.Error.Message.ShouldContain("G1_sessions insufficient");
        var ok = RampEvent.OwnerSetStep(Account, current, 2, "owner:hem", "ok", Eligible());
        ok.Value.Gates.ShouldNotBeNull().Hash.Length.ShouldBe(64);
        ok.Value.Kind.ShouldBe(RampEventKinds.SetStep);
    }

    [Fact]
    public void A_raise_is_refused_under_a_hold_and_s3_needs_the_exit_confirmation()
    {
        RampEvent.OwnerSetStep(Account, new RampState(1, 1, true), 2, "owner:hem", "ok", Eligible()).Error.Code.ShouldBe(RampErrors.HoldActiveCode);
        RampEvent.OwnerSetStep(Account, new RampState(1, 2, false), 3, "owner:hem", "ok", Eligible()).Error.Code.ShouldBe(RampErrors.ExitNotConfirmedCode);
        RampEvent.OwnerSetStep(Account, new RampState(1, 2, false, ExitConfirmed: true), 3, "owner:hem", "ok", Eligible()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_rule_writes_hold_step_down_and_an_auto_step_up_that_stops_at_s2()
    {
        var s1 = new RampState(1, 1, false);
        RampEvent.RuleHold(Account, s1, "B2").Hold.ShouldBeTrue();
        RampEvent.RuleHold(Account, s1, "B2").Actor.ShouldBe(RampEventKinds.RuleActor);
        var down = RampEvent.RuleStepDown(Account, new RampState(2, 2, true), "B6");
        down.ToStep.ShouldBe(1);
        down.Hold.ShouldBeFalse();
        var settings = new LiveSettings(7, new string('a', 64), false, true);
        var up = RampEvent.RuleAutoStepUp(Account, s1, Eligible(), settings).ShouldNotBeNull();
        up.ToStep.ShouldBe(2);
        up.SettingsId.ShouldBe(7);
        RampEvent.RuleAutoStepUp(Account, new RampState(3, 2, false), Eligible(), settings).ShouldBeNull(); // never above step 2
        RampEvent.RuleAutoStepUp(Account, s1, Eligible(), LiveSettings.Defaults).ShouldBeNull(); // OFF writes nothing
        RampEvent.RuleAutoStepUp(Account, new RampState(1, 1, true), Eligible(), settings).ShouldBeNull(); // a hold blocks it
        RampEvent.RuleAutoStepUp(Account, s1, GateSnapshot.From(1, RampGates.Evaluate(new RampEvidence(), 1, T)), settings).ShouldBeNull();
    }

    [Fact]
    public void A_hold_is_cleared_by_the_owner_with_a_reason_while_nothing_still_holds_it()
    {
        var held = new RampState(5, 1, true);
        var still = new[] { new Breach("B5", Breach.Hold, "x") };
        RampEvent.OwnerClearHold(Account, held, "owner:hem", "looked", still).Error.Code.ShouldBe(RampErrors.HoldConditionActiveCode);
        RampEvent.OwnerClearHold(Account, held, "owner:hem", " ", []).IsFailure.ShouldBeTrue();
        RampEvent.OwnerClearHold(Account, held, "rule:ramp", "x", []).Error.Code.ShouldBe("live.owner_only");
        RampEvent.OwnerClearHold(Account, new RampState(5, 1, false), "owner:hem", "x", []).Error.Code.ShouldBe(RampErrors.NoHoldCode);
        var ok = RampEvent.OwnerClearHold(Account, held, "owner:hem", "looked", []).Value;
        ok.Kind.ShouldBe(RampEventKinds.ClearHold);
        ok.Hold.ShouldBeFalse();
    }

    [Fact]
    public void The_thresholds_are_valid_by_default_and_refuse_nonsense()
    {
        T.IsValid.ShouldBeTrue();
        (T with { MinProtectedShare = 1.5 }).IsValid.ShouldBeFalse();
        (T with { MaxSlippageUpperR = 0.01 }).IsValid.ShouldBeFalse();
        (T with { MaxDrawdownR = 0 }).IsValid.ShouldBeFalse();
    }
}
