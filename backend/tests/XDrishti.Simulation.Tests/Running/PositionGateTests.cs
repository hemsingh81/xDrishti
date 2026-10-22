using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 10 of P3-03d (D11): the rows G1 to G5 of the position gate, with hand-made candidates and the real walker. One instrument, BUY, decisions at the :15 and :45 of a 30m strategy, the prices of setup A moved to the decision
/// times, a hold of 5 sessions unless the row says otherwise. G6 (a trade that P3-03c skips for quantity still blocks) is written by the slice that merges second; here the gate has no quantity input, which a signature test pins.
/// </summary>
public sealed class PositionGateTests
{
    private static readonly TickSpec Tick = TickSpec.FromRupees(0.05m).Value;

    private static EntryCandidate Candidate(IReadOnlyList<Bar> bars, DateOnly date, string decision, double stop, double triggerClose)
    {
        var own = Day.Epoch(date, Day.Minute(decision) - 1);
        var index = bars.ToList().FindIndex(b => b.EpochSeconds == own);
        index.ShouldBeGreaterThanOrEqualTo(0, $"{date} {decision}: the candidate's own minute must be in the list");
        return Cand.Make(Side.Buy, index, decision, stop, "09:45", (triggerClose, triggerClose, triggerClose, triggerClose), date: date);
    }

    private static WalkRules Rules(int sessions, double rewardRisk = 2.0) =>
        new(rewardRisk, WalkRules.NoSquareOff, FillRules.Default, Tick, SwingHold.Create(sessions, null).Value);

    private static List<CandidateOutcome> Apply(IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, WalkRules rules, List<EntryCandidate>? walked = null)
    {
        var result = PositionGate.Apply(candidates, c =>
        {
            walked?.Add(c);
            return TradeWalker.Walk(c, bars, rules);
        });
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        return [.. result.Value];
    }

    private static WalkCounters Sum(IEnumerable<CandidateOutcome> outcomes) => outcomes.Aggregate(WalkCounters.Empty, (sum, o) => sum.Add(o.Walk.ToCounters()));

    /// <summary>The week of G1: Monday 10:15 fill, flat 100.20, Tuesday flat, Wednesday a minute at 10:15 that touches the stop 99.00 (not a gap), flat 99.00 after it.</summary>
    private static Bar[] G1Bars() => Day.Join(
        Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"),
        Day.Dense(Tue).Start(100.20),
        Day.Dense(Wed).Start(100.20).Set("10:15 99.40/99.50/98.90/99.00"));

    [Fact]
    public void G1_a_candidate_decided_before_the_exit_minute_or_at_it_is_blocked_and_one_decided_after_it_is_walked()
    {
        var bars = G1Bars();
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c2 = Candidate(bars, Tue, "10:15", 99.00, 100.20);
        var c3 = Candidate(bars, Wed, "10:15", 98.00, 99.00);
        var c4 = Candidate(bars, Wed, "10:45", 98.00, 99.00);
        var walked = new List<EntryCandidate>();

        var outcomes = Apply(bars, [c1, c2, c3, c4], Rules(5), walked);

        outcomes[0].Walk.Trade!.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("10:15")));
        outcomes[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.Stop);
        outcomes[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen);
        outcomes[2].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen, "D = E: the first minute the new order could fill in is the very minute in which the old position leaves");
        outcomes[3].Walk.Trade.ShouldNotBeNull("D = E + 1800: walked, and it fills");
        walked.ShouldBe([c1, c4], "a blocked candidate is not walked");
        var counters = Sum(outcomes);
        (counters.Candidates, counters.PositionOpen, counters.Trades, counters.LimitNotReached).ShouldBe((4L, 2L, 2L, 0L));
    }

    [Fact]
    public void G1_a_decision_one_minute_after_the_exit_minute_starts_is_walked_the_position_has_left_by_the_end_of_its_exit_minute()
    {
        var bars = G1Bars();
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c3 = Candidate(bars, Wed, "10:16", 98.00, 99.00);

        var outcomes = Apply(bars, [c1, c3], Rules(5));

        outcomes[1].Walk.NoTrade.ShouldBeNull("D = E + 60");
        outcomes[1].Walk.Trade.ShouldNotBeNull();
    }

    [Fact]
    public void A_blocked_candidate_adds_one_candidate_and_one_position_open_and_nothing_else()
    {
        var bars = G1Bars();
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c2 = Candidate(bars, Tue, "10:15", 99.00, 100.20);

        var blocked = Apply(bars, [c1, c2], Rules(5))[1].Walk.ToCounters();

        blocked.ShouldBe(WalkCounters.Empty with { Candidates = 1, PositionOpen = 1 });
    }

    [Fact]
    public void G2_after_a_trade_that_ended_with_the_data_every_later_candidate_of_the_list_is_blocked()
    {
        var bars = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20));
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c2 = Candidate(bars, Tue, "14:45", 99.00, 100.20);
        var c3 = Candidate(bars, Tue, "15:15", 99.00, 100.20);

        var outcomes = Apply(bars, [c1, c2, c3], Rules(5));

        outcomes[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.EndOfData);
        outcomes[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen);
        outcomes[2].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen);
    }

    [Fact]
    public void G2b_a_position_that_left_at_the_last_known_close_holds_its_instrument_until_the_end_of_its_due_session_in_the_short_run_and_in_the_long_one()
    {
        // N = 3: due Wednesday 15:29, which has volume 0, as has every minute from 15:11; the last held minute is 15:10.
        Day Wednesday()
        {
            var wed = Day.Dense(Wed).Start(100.20);
            for (var minute = Day.Minute("15:11"); minute <= Day.Minute("15:29"); minute++)
            {
                wed.SetEmpty(Day.Clock(minute) + " 100.20/100.20/100.20/100.20");
            }

            return wed;
        }

        var mon = Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20");
        var tue = Day.Dense(Tue).Start(100.20);
        var longRun = Day.Join(mon, tue, Wednesday(), Day.Dense(Thu).Start(100.20));
        var shortRun = Day.Join(mon, tue, Wednesday());

        foreach (var (bars, name) in new[] { (longRun, "long"), (shortRun, "short") })
        {
            var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
            var c2 = Candidate(bars, Wed, "15:15", 99.00, 100.20);
            var c3 = Candidate(bars, Wed, "15:30", 99.00, 100.20);
            var outcomes = Apply(bars, [c1, c2, c3], Rules(3));

            outcomes[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen, name + " C2");
            outcomes[2].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen, name + " C3 (D = E)");
            if (name == "long")
            {
                outcomes[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.MaxHold);
                outcomes[0].Walk.MaxHoldAtLastClose.ShouldBeTrue();
                outcomes[0].Walk.Trade!.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:10")));
                outcomes[0].Walk.HoldEndEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:30")));
                var c4 = Candidate(bars, Thu, "10:15", 99.00, 100.20);
                Apply(bars, [c1, c2, c3, c4], Rules(3))[3].Walk.NoTrade.ShouldBeNull("C4, decided the next day, is walked in the long run");
            }
            else
            {
                outcomes[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.EndOfData);
            }
        }
    }

    [Fact]
    public void G2b_control_with_the_last_print_as_the_hold_end_the_planted_decisions_would_be_walked_in_the_long_run_only()
    {
        // The first draft's rule: E = the exit instant. A decision at 15:15 would then be walked after a MaxHoldAtLastClose exit at 15:10 and would be blocked in the short run (EndOfData): the two runs would disagree.
        var wed = Day.Dense(Wed).Start(100.20);
        for (var minute = Day.Minute("15:11"); minute <= Day.Minute("15:29"); minute++)
        {
            wed.SetEmpty(Day.Clock(minute) + " 100.20/100.20/100.20/100.20");
        }

        var bars = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20), wed, Day.Dense(Thu).Start(100.20));
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c2 = Candidate(bars, Wed, "15:15", 99.00, 100.20);
        var first = TradeWalker.Walk(c1, bars, Rules(3)).Value;

        (c2.DecisionEpochSeconds <= first.Trade!.ExitEpochSeconds).ShouldBeFalse("with E = the last print, C2 would not be blocked");
        (c2.DecisionEpochSeconds <= first.HoldEndEpochSeconds).ShouldBeTrue("with the end of the due session it is");
    }

    [Fact]
    public void G3_the_gate_comes_before_the_walk_so_a_candidate_that_would_have_missed_its_limit_is_a_position_open()
    {
        // C1 stays open on Tuesday (reward-to-risk 10: target 110); C2's fill minute opens far above its limit and never trades back to it.
        var bars = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"), Day.Dense(Tue).Start(100.20).Set("10:15 103.00/103.20/102.90/103.10"));
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c2 = Candidate(bars, Tue, "10:15", 99.00, 100.20);

        var alone = TradeWalker.Walk(c2, bars, Rules(5, 10.0)).Value;
        var outcomes = Apply(bars, [c1, c2], Rules(5, 10.0));

        alone.NoTrade.ShouldBe(NoTradeReason.LimitNotReached, "walked on its own it misses");
        outcomes[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen);
        Sum(outcomes).LimitNotReached.ShouldBe(0);
    }

    [Fact]
    public void A_no_trade_is_not_a_position_so_the_next_candidate_is_not_blocked_by_it()
    {
        // C1 misses its limit (LimitNotReached); C2, decided a minute later, is walked.
        var bars = Day.Join(Day.Dense(Mon).Start(100.00).Set("10:15 103.00/103.20/102.90/103.10"), Day.Dense(Tue).Start(103.10));
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var c2 = Candidate(bars, Mon, "10:30", 99.00, 103.10);

        var outcomes = Apply(bars, [c1, c2], Rules(5));

        outcomes[0].Walk.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
        outcomes[1].Walk.NoTrade.ShouldNotBe(NoTradeReason.PositionOpen);
        outcomes[1].Walk.Trade.ShouldNotBeNull();
    }

    [Fact]
    public void A_failure_of_a_walk_is_returned_as_it_is_and_nothing_after_it_is_walked()
    {
        var bars = G1Bars();
        var c1 = Candidate(bars, Mon, "10:15", 99.00, 100.00);
        var walked = 0;

        var result = PositionGate.Apply([c1, c1], _ =>
        {
            walked++;
            return Result.Failure<WalkOutcome>(Error.Validation("simulation.rules_invalid", "x"));
        });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.rules_invalid");
        walked.ShouldBe(1);
    }

    [Fact]
    public void G4_an_intraday_strategy_is_not_gated_the_end_of_the_data_of_day_one_does_not_block_day_two()
    {
        // ORB over three consecutive days: day 1 ends at 15:09 so that its trade is EndOfData (F6), then two dense days with a breakout each.
        var bars = ReplayDays.Join(ReplayDays.F6(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F1(ReplayDays.D3));

        var result = Sim.Run(OrbV1.Create(), bars);

        result.Walk.PositionOpen.ShouldBe(0);
        result.Walk.ExitsEndOfData.ShouldBe(1);
        result.Trades.Count.ShouldBe(2);
        result.OpenAtEnd.Count.ShouldBe(1);
        result.Outcomes.ShouldAllBe(o => o.Walk.NoTrade != NoTradeReason.PositionOpen);
    }

    [Fact]
    public void G4_no_intraday_run_over_100_seeded_multi_day_lists_has_a_position_open()
    {
        var simulator = Sim.Create(OrbV1.Create());
        var walked = 0L;

        for (ulong seed = 1; seed <= 100; seed++)
        {
            var result = Sim.Run(simulator, SeededSessions.Of(seed).Bars);
            result.Walk.PositionOpen.ShouldBe(0, "seed " + seed);
            result.Walk.SessionsHeld.ShouldBe(result.Walk.Trades, "seed " + seed + ": an intraday trade holds one session");
            result.Walk.SessionsUnseen.ShouldBe(0, "seed " + seed);
            walked += result.Walk.Candidates;
        }

        walked.ShouldBeGreaterThan(100, "the lists must make candidates for this to compare anything");
    }

    [Fact]
    public void G5_the_same_candidates_walked_with_two_reward_to_risk_values_give_other_trades_and_other_gates_and_the_candidate_holds_no_exit()
    {
        var bars = Day.Join(
            Day.Dense(Mon).Start(100.00).Set("10:15 100.00/100.30/99.80/100.20"),
            Day.Dense(Tue).Start(100.20).Set("11:00 101.00/102.50/100.90/102.00"),
            Day.Dense(Wed).Start(102.00));
        var candidates = new[] { Candidate(bars, Mon, "10:15", 99.00, 100.00), Candidate(bars, Tue, "14:45", 99.00, 102.00) };

        var near = Apply(bars, candidates, Rules(5, 2.0));
        var far = Apply(bars, candidates, Rules(5, 10.0));

        near.Select(o => o.Candidate).ShouldBe(candidates, "the candidates are identical");
        far.Select(o => o.Candidate).ShouldBe(candidates);
        near[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.Target, "the closer target is reached on Tuesday");
        near[1].Walk.Trade.ShouldNotBeNull("so the position has left when the second candidate is decided");
        far[0].Walk.Trade!.ExitReason.ShouldBe(ExitReason.EndOfData);
        far[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen, "a longer hold blocks more");
        typeof(EntryCandidate).GetProperties().Select(p => p.Name.ToLowerInvariant()).ShouldNotContain(n => n.Contains("exit", StringComparison.Ordinal) || n.Contains("target", StringComparison.Ordinal) || n.Contains("hold", StringComparison.Ordinal));
    }

    [Fact]
    public void P4_05_the_count_of_the_entries_the_gate_would_block_on_ungated_walks_equals_the_position_open_fates_of_the_gated_run()
    {
        var bars = G1Bars();
        var candidates = new[] { Candidate(bars, Mon, "10:15", 99.00, 100.00), Candidate(bars, Tue, "10:15", 99.00, 100.20), Candidate(bars, Wed, "10:15", 98.00, 99.00), Candidate(bars, Wed, "10:45", 98.00, 99.00) };
        var rules = Rules(5);
        var gated = Apply(bars, candidates, rules);
        var ungated = candidates.Select(c => new CandidateOutcome(c, TradeWalker.Walk(c, bars, rules).Value)).ToList();

        var blocked = PositionGate.CountBlocked(ungated, _ => true);
        var inWindow = PositionGate.CountBlocked(ungated, c => c.DecisionBarIndex != candidates[1].DecisionBarIndex);

        blocked.ShouldBe(gated.Count(o => o.Walk.NoTrade == NoTradeReason.PositionOpen));
        blocked.ShouldBe(2);
        inWindow.ShouldBe(1, "a blocked entry outside the window is not counted");
    }

    [Fact]
    public void G6_the_gate_has_no_quantity_or_cost_input_so_a_sizing_skip_cannot_open_it()
    {
        var apply = typeof(PositionGate).GetMethod(nameof(PositionGate.Apply))!;

        apply.GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(IEnumerable<EntryCandidate>), typeof(Func<EntryCandidate, Result<WalkOutcome>>)]);
        typeof(PositionGate).GetProperties().Select(p => p.Name).ShouldBeEmpty("the gate exposes no state that a sizing skip could set");
    }
}
