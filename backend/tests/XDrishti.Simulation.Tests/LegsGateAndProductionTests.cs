using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Oracle;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>Criteria 9 and 13 of P4-01: the position gate holds an instrument until the last leg of a trade with legs, and no production path makes a leg.</summary>
public sealed class LegsGateAndProductionTests
{
    private static readonly DateOnly Monday = DayBuilder.Monday;

    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    [Fact]
    public void A_candidate_decided_between_the_first_and_the_last_leg_is_refused_and_one_decided_after_the_last_leg_is_walked()
    {
        // the trade of H12: BUY at 100.50 on Monday 09:45, half out at 10:00, the rest at the open of Tuesday 09:15 (a gap through the stop the 15:30 bar moved)
        var monday = Day.Dense(Monday).Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:50 100.50/101.00/100.50/101.00", "09:55 101.00/102.00/101.00/102.00", "09:59 102.00/103.25/102.00/103.25", "10:00 103.25/103.50/103.00/103.25");
        var tuesday = Day.After(monday, Tuesday).Set("09:15 101.00/101.50/100.75/101.25");
        var bars = monday.Bars().Concat(tuesday.Bars()).ToList();
        var tape = new ExitTape([TestLadderRule.LowFeature, TestLadderRule.HighFeature]);
        tape.Append(DayBuilder.Epoch(Monday, "09:45"), 100.50, [99.00, 100.50]);
        tape.Append(DayBuilder.Epoch(Monday, "10:00"), 103.25, [100.25, 103.25]);
        tape.Append(DayBuilder.Epoch(Monday, "15:30"), 103.25, [102.00, 103.50]);
        var rules = new WalkRules(null, WalkRules.NoSquareOff, Cand.NoSlippage, Cand.Tick05, SwingHold.Create(3, TestCalendars.DefaultWeek()).Value);
        var first = Cand.B();
        var during = Cand.Make(Side.Buy, DayBuilder.IndexOf(bars, Monday, "10:59"), "11:00", 98.00, "10:45", (101.00, 103.25, 101.00, 103.25));
        var after = Cand.Make(Side.Buy, DayBuilder.IndexOf(bars, Tuesday, "09:59"), "10:00", 98.00, "09:45", (101.25, 101.25, 101.25, 101.25), date: Tuesday);

        var gated = PositionGate.Apply([first, during, after], c => TradeWalker.Walk(c, bars, rules, new ExitContext(new TestLadderRule(0.5m), tape)));

        gated.IsSuccess.ShouldBeTrue();
        var outcomes = gated.Value;
        outcomes[0].Walk.Trade.ShouldNotBeNull().Legs.ShouldNotBeNull().Count.ShouldBe(3);
        outcomes[0].Walk.HoldEndEpochSeconds.ShouldBe(DayBuilder.Epoch(Tuesday, "09:15"), "the position is held until the last leg");
        outcomes[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen, "decided between the first leg (Monday 10:00) and the last (Tuesday 09:15)");
        outcomes[2].Walk.NoTrade.ShouldBeNull("decided after the last leg");
        outcomes[2].Walk.Trade.ShouldNotBeNull();
    }

    [Fact]
    public void No_run_of_a_built_in_or_test_strategy_through_the_simulator_makes_a_leg()
    {
        var series = 0;
        var trades = 0;
        foreach (var (name, prepare, _) in ComparisonCases.ReplayStrategies)
        {
            var prepared = prepare(false);
            foreach (var one in SeededReplay.All.Take(40))
            {
                var (replay, segments) = SeededReplay.Replay(prepared, one.Bars, one.Dates[0], one.Dates[^1]);
                series++;
                replay.Trades.Concat(replay.OpenAtEnd).ShouldAllBe(t => t.Legs == null, name);
                foreach (var segment in segments)
                {
                    (segment.Result.Walk.LegsTotal, segment.Result.Walk.LegsMerged, segment.Result.Walk.StopLoosenIgnored).ShouldBe((0L, 0L, 0L), name + " " + one.Seed);
                }

                trades += replay.Trades.Count;
            }
        }

        series.ShouldBe(120);
        trades.ShouldBeGreaterThan(50, "the runs make trades; they make none with legs");
    }

    [Fact]
    public void The_seeded_hand_days_make_no_leg_either_because_no_rule_is_given_to_the_walker()
    {
        var withLegs = SeededDays.All.SelectMany(d => d.Walks.Select(w => TradeWalker.Walk(w.Candidate, d.Bars, w.Rules).Value.Trade)).Count(t => t?.Legs is not null);

        withLegs.ShouldBe(0);
    }
}
