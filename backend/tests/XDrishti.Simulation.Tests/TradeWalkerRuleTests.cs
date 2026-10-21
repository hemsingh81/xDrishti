using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>The rows W1 to W14 of the walker table (AC3, AC4) with hand-made candidates and bars: no frame, no compiler, nothing mocked.</summary>
public sealed class TradeWalkerRuleTests
{
    public static TheoryData<string> Rows => [.. Cases.Rows.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_row_of_the_walker_table_gives_its_result(string name)
    {
        var walkCase = Cases.W(name);

        foreach (var item in walkCase.Items)
        {
            WalkAssert.Matches(walkCase, item, WalkAssert.Walk(walkCase, item));
        }
    }

    [Theory]
    [InlineData("W6", 1, 0)]
    [InlineData("W7", 0, 1)]
    [InlineData("W13", 0, 1)]
    [InlineData("W5", 0, 0)]
    public void The_gap_counters_count_only_exits_at_a_level_the_minute_opened_beyond(string name, long stopGaps, long targetGaps)
    {
        var walkCase = Cases.W(name);

        var counters = WalkAssert.Walk(walkCase, walkCase.Items[0]).Value.ToCounters();

        counters.StopGapExits.ShouldBe(stopGaps);
        counters.TargetGapExits.ShouldBe(targetGaps);
    }

    [Fact]
    public void A_stop_touched_exactly_and_a_target_touched_exactly_count()
    {
        var stop = Cases.W("W5");
        var target = Cases.F("F1");

        WalkAssert.Walk(stop, stop.Items[0]).Value.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.Stop);
        WalkAssert.Walk(target, target.Items[0]).Value.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.Target);
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F2")]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    [InlineData("F6")]
    [InlineData("F7")]
    [InlineData("F9")]
    [InlineData("F13")]
    [InlineData("F14")]
    [InlineData("F15")]
    [InlineData("F16")]
    [InlineData("F17")]
    [InlineData("F17b-first")]
    [InlineData("F17b-second")]
    [InlineData("W1")]
    [InlineData("W3")]
    [InlineData("W6")]
    [InlineData("W7")]
    [InlineData("W13")]
    [InlineData("W14-opens-beyond-the-stop")]
    [InlineData("W14-opens-beyond-the-target")]
    public void The_sell_mirror_of_a_buy_row_gives_the_mirrored_result(string name)
    {
        // Prices p become 200 - p (exact for multiples of 0.25), high and low swap, the side flips (the trigger bar is mirrored too, so that the limit does not bind): R, MAE, MFE, the reason, the minutes and the flags must not change.
        var walkCase = Cases.All.Single(c => c.Name == name);
        var mirroredBars = walkCase.Bars.Select(b => b with { Open = 200 - b.Open, High = 200 - b.Low, Low = 200 - b.High, Close = 200 - b.Close }).ToList();

        foreach (var item in walkCase.Items.Where(i => i.Candidate.Side == Side.Buy))
        {
            var trigger = item.Candidate.TriggerBar;
            var mirroredTrigger = trigger with { Open = 200 - trigger.Open, High = 200 - trigger.Low, Low = 200 - trigger.High, Close = 200 - trigger.Close };
            var mirroredCandidate = item.Candidate with { Side = Side.Sell, StopLevel = 200 - item.Candidate.StopLevel, TriggerBar = mirroredTrigger };

            var buy = WalkAssert.Walk(walkCase, item);
            var sell = TradeWalker.Walk(mirroredCandidate, mirroredBars, walkCase.Rules);

            sell.IsSuccess.ShouldBeTrue();
            sell.Value.BarsExamined.ShouldBe(buy.Value.BarsExamined);
            sell.Value.NoTrade.ShouldBe(buy.Value.NoTrade);
            sell.Value.EntryAfterMissingMinutes.ShouldBe(buy.Value.EntryAfterMissingMinutes);
            sell.Value.StopGapExit.ShouldBe(buy.Value.StopGapExit);
            sell.Value.TargetGapExit.ShouldBe(buy.Value.TargetGapExit);
            sell.Value.AmbiguousMinute.ShouldBe(buy.Value.AmbiguousMinute);
            sell.Value.LateSquareOff.ShouldBe(buy.Value.LateSquareOff);
            if (buy.Value.Trade is { } b)
            {
                var s = sell.Value.Trade.ShouldNotBeNull();
                s.EntryPrice.ShouldBe(200 - b.EntryPrice);
                s.ExitPrice.ShouldBe(200 - b.ExitPrice);
                s.TargetPrice.ShouldBe(b.TargetPrice is { } t ? 200 - t : null);
                s.RExact.ShouldBe(b.RExact);
                s.MaeR.ShouldBe(b.MaeR);
                s.MfeR.ShouldBe(b.MfeR);
                s.ExitReason.ShouldBe(b.ExitReason);
                s.ExitEpochSeconds.ShouldBe(b.ExitEpochSeconds);
                s.EntryEpochSeconds.ShouldBe(b.EntryEpochSeconds);
            }
        }
    }

    [Fact]
    public void A_list_with_no_minutes_has_no_candidate_in_it()
    {
        var result = TradeWalker.Walk(Cand.B(), [], Cand.Standard);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.candidate_not_in_bars");
    }

    [Fact]
    public void A_single_minute_that_is_the_candidates_own_has_no_fill_minute()
    {
        var bars = DayBuilder.Prefix().Until("09:44").Build().Skip(29).ToList();
        var candidate = Cand.B() with { DecisionBarIndex = 0 };

        var outcome = TradeWalker.Walk(candidate, bars, Cand.Standard).Value;

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000)]
    [InlineData(28)]
    [InlineData(30)]
    public void A_candidate_whose_minute_is_not_at_its_index_is_a_failure(int index)
    {
        var f1 = Cases.F("F1");

        var result = TradeWalker.Walk(f1.Items[0].Candidate with { DecisionBarIndex = index }, f1.Bars, f1.Rules);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.candidate_not_in_bars");
    }

    [Fact]
    public void A_candidate_whose_minute_does_not_end_at_the_decision_instant_is_a_failure()
    {
        var f1 = Cases.F("F1");

        var result = TradeWalker.Walk(f1.Items[0].Candidate with { DecisionEpochSeconds = f1.Items[0].Candidate.DecisionEpochSeconds + 60 }, f1.Bars, f1.Rules);

        result.Error.Code.ShouldBe("simulation.candidate_not_in_bars");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_stop_that_is_not_a_finite_number_is_invalid(double stop)
    {
        var f1 = Cases.F("F1");

        var result = TradeWalker.Walk(f1.Items[0].Candidate with { StopLevel = stop }, f1.Bars, f1.Rules);

        result.Error.Code.ShouldBe("simulation.rules_invalid");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_reward_risk_that_is_not_above_zero_is_invalid(double rewardRisk)
    {
        var f1 = Cases.F("F1");

        var result = TradeWalker.Walk(f1.Items[0].Candidate, f1.Bars, f1.Rules with { RewardRisk = rewardRisk });

        result.Error.Code.ShouldBe("simulation.rules_invalid");
    }

    [Fact]
    public void Two_equal_epochs_among_the_visited_minutes_are_a_failure()
    {
        var f1 = Cases.F("F1");
        var bars = f1.Bars.ToList();
        bars[40] = bars[40] with { EpochSeconds = bars[39].EpochSeconds };

        var result = TradeWalker.Walk(f1.Items[0].Candidate, bars, f1.Rules);

        result.Error.Code.ShouldBe("simulation.bars_not_ascending");
    }

    [Fact]
    public void A_descending_pair_among_the_visited_minutes_is_a_failure()
    {
        var f1 = Cases.F("F1");
        var bars = f1.Bars.ToList();
        bars[40] = bars[40] with { EpochSeconds = bars[38].EpochSeconds };

        var result = TradeWalker.Walk(f1.Items[0].Candidate, bars, f1.Rules);

        result.Error.Code.ShouldBe("simulation.bars_not_ascending");
    }

    [Fact]
    public void A_fill_minute_that_is_not_after_the_candidates_own_is_a_failure()
    {
        var f1 = Cases.F("F1");
        var bars = f1.Bars.ToList();
        bars[30] = bars[30] with { EpochSeconds = bars[29].EpochSeconds };

        var result = TradeWalker.Walk(f1.Items[0].Candidate, bars, f1.Rules);

        result.Error.Code.ShouldBe("simulation.bars_not_ascending");
    }

    [Fact]
    public void Equal_epochs_before_the_candidate_or_after_the_exit_are_not_the_walkers_business()
    {
        var f1 = Cases.F("F1");
        var expected = OutcomeFormatter.Render(WalkAssert.Walk(f1, f1.Items[0]));
        var bars = f1.Bars.ToList();
        bars[5] = bars[5] with { EpochSeconds = bars[4].EpochSeconds };
        bars[200] = bars[200] with { EpochSeconds = bars[199].EpochSeconds };

        var result = TradeWalker.Walk(f1.Items[0].Candidate, bars, f1.Rules);

        OutcomeFormatter.Render(result).ShouldBe(expected);
    }

    [Fact]
    public void A_hole_of_forty_minutes_before_the_entry_is_no_trade()
    {
        var bars = Cases.F1Day().Absent("09:45", "10:24").Build();

        var outcome = TradeWalker.Walk(Cand.B(), bars, Cand.Standard).Value;

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        outcome.BarsExamined.ShouldBe(0);
    }

    [Fact]
    public void A_day_that_ends_before_the_square_off_ends_at_the_last_close_and_the_next_date_is_not_read()
    {
        var w11 = Cases.W("W11");
        var f6 = Cases.F("F6");

        var withNextDate = OutcomeFormatter.Render(WalkAssert.Walk(w11, w11.Items[0]));
        var alone = OutcomeFormatter.Render(WalkAssert.Walk(f6, f6.Items[0]));

        withNextDate.ShouldBe(alone);
    }

    [Fact]
    public void A_candidate_that_is_the_same_object_walked_twice_gives_the_same_text()
    {
        var f5 = Cases.F("F5");

        OutcomeFormatter.Render(WalkAssert.Walk(f5, f5.Items[0])).ShouldBe(OutcomeFormatter.Render(WalkAssert.Walk(f5, f5.Items[0])));
    }

    [Fact]
    public void The_fill_delay_bound_is_read_from_the_engine()
    {
        SimulationEngine.MaxFillDelayMinutes.ShouldBe(5);
        SimulationEngine.Version.ShouldBe("sim-6");
    }

    [Theory]
    [InlineData("09:46", true)]
    [InlineData("09:50", true)]
    [InlineData("09:51", false)]
    [InlineData("09:45", true)]
    public void A_fill_minute_zero_one_and_five_minutes_after_the_decision_fills_and_six_does_not(string firstMinuteAfter, bool fills)
    {
        // The minutes between the decision (09:45) and the fill minute are missing; whatever the price of the fill minute, one more than five minutes after the instant is no trade.
        var day = Cases.F1Day();
        if (firstMinuteAfter != "09:45")
        {
            day.Absent("09:45", ToTime(DayBuilder.MinuteOfDay(firstMinuteAfter) - 1));
        }

        var outcome = TradeWalker.Walk(Cand.B(), day.Build(), Cand.Standard).Value;

        (outcome.Trade is not null).ShouldBe(fills);
        outcome.NoTrade.ShouldBe(fills ? null : NoTradeReason.NoFillBar);
    }

    private static string ToTime(int minuteOfDay) => $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    [Fact]
    public void The_walker_reads_only_the_close_of_the_trigger_bar()
    {
        // The walker reads the close of the trigger bar (the entry limit is set from it) and nothing else of it, and never the trace: a candidate with an empty trace and a trigger bar of nonsense but for its close gives the same trade as F1.
        var f1 = Cases.F("F1");
        var trigger = f1.Items[0].Candidate.TriggerBar;
        var candidate = f1.Items[0].Candidate with
        {
            TriggerBar = new Bar(0, -5, 1e9, 7, trigger.Close, -1),
            Trace = new DecisionTrace([new TraceItem("buy.trigger[0]", "x", TraceTruth.Unknown, [])]),
        };

        var outcome = TradeWalker.Walk(candidate, f1.Bars, f1.Rules).Value;

        outcome.Trade.ShouldNotBeNull().RExact.ShouldBe(2.0);
        outcome.Trade.TriggerTimeframe.ShouldBe(Timeframe.M15);
    }
}
