using XDrishti.Domain.Research;
using static XDrishti.Simulation.Tests.Support.BaseRun;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>Criteria 5 to 9 of P3-06: the hand-calculated rows P1 to P10 of D11 through <c>Simulator.Run</c>, in both fill modes. The numbers are the spec's, computed by hand.</summary>
public sealed class BaseBreakoutRowsTests
{
    private const double Tolerance = 5e-5;

    private static long At(int session, string clock) => Day.Epoch(BaseDays.Date(session), Day.Minute(clock));

    private static Day[] Series(params Day[] tail) => [.. BaseDays.ThroughSignal(), .. tail];

    [Fact]
    public void P1_the_target_is_hit_on_the_third_session_the_exact_row()
    {
        var result = Go(Series(P1Tail()));

        var outcome = result.Outcomes.ShouldHaveSingleItem();
        outcome.Candidate.DecisionEpochSeconds.ShouldBe(At(70, "15:30"));
        outcome.Candidate.StopLevel.ShouldBe(99.40, Tolerance);
        var trade = outcome.Walk.Trade.ShouldNotBeNull();
        trade.TradeDate.ShouldBe(BaseDays.Date(71));
        trade.Key.ShouldEndWith("|2026-04-13");
        (trade.EntryFill, trade.EntryPrice, trade.StopPrice).ShouldBe((EntryFill.AtOpen, 102.05, 99.40));
        trade.EntryEpochSeconds.ShouldBe(At(71, "09:15"));
        trade.TargetPrice.ShouldNotBeNull().ShouldBe(107.35, Tolerance);
        (trade.ExitReason, trade.ExitEpochSeconds).ShouldBe((ExitReason.Target, At(73, "10:00")));
        trade.ExitPrice.ShouldBe(107.35, Tolerance);
        trade.SessionsHeld.ShouldBe(3);
        trade.RExact.ShouldBe(2.0, Tolerance);
        trade.MaeR.ShouldBe(0.0943, 1e-4);
        trade.MfeR.ShouldBe(2.0, Tolerance);
        result.Walk.OvernightEntries.ShouldBe(1);
        result.Scans.Single().Candidates.ShouldBe(1);
        result.Scans.Single().LaterTriggers.ShouldBe(0);
    }

    [Fact]
    public void P1_modelled_fills_give_the_filled_entry_and_the_slippage_of_the_row()
    {
        var trade = Go(Series(P1Tail()), exact: false).Trades.ShouldHaveSingleItem();

        trade.EntryFilledPrice.ShouldBe(102.10, Tolerance);
        trade.RFilled.ShouldBe(1.9811, 1e-4);
        trade.SlippageR.ShouldBe(-0.0189, 1e-4);
        trade.RExact.ShouldBe(2.0, Tolerance);
    }

    [Fact]
    public void P2_a_gap_through_the_stop_exits_at_the_open_of_the_second_session()
    {
        var tail = new[] { T(1, 102.05, 103.20, 101.80, 103.00), T(2, 98.50, 98.80, 98.20, 98.60) };

        var exact = Go(Series(tail));
        var modelled = Go(Series(tail), exact: false);

        var trade = exact.Trades.ShouldHaveSingleItem();
        (trade.ExitReason, trade.ExitPrice, trade.SessionsHeld).ShouldBe((ExitReason.Stop, 98.50, 2));
        trade.RExact.ShouldBe(-1.3396, 1e-4);
        trade.MfeR.ShouldBe(0.4340, 1e-4);
        exact.Walk.OvernightStopGaps.ShouldBe(1);
        var slipped = modelled.Trades.ShouldHaveSingleItem();
        slipped.ExitFilledPrice.ShouldBe(98.35, Tolerance);
        slipped.RFilled.ShouldBe(-1.4151, 1e-4);
        slipped.SlippageR.ShouldBe(-0.0755, 1e-4);
    }

    [Fact]
    public void P3_the_fifth_session_leaves_at_its_close_with_max_hold()
    {
        var tail = new[]
        {
            T(1, 102.05, 103.20, 101.80, 103.00), T(2, 103.00, 103.40, 102.20, 102.80), T(3, 102.80, 104.00, 102.40, 102.70), T(4, 102.70, 103.60, 102.30, 103.50), T(5, 103.50, 104.10, 102.90, 103.20),
        };

        var exact = Go(Series(tail));
        var modelled = Go(Series(tail), exact: false);

        var trade = exact.Trades.ShouldHaveSingleItem();
        (trade.ExitReason, trade.SessionsHeld, trade.ExitEpochSeconds).ShouldBe((ExitReason.MaxHold, 5, At(75, "15:29")));
        trade.ExitPrice.ShouldBe(103.20, Tolerance);
        trade.RExact.ShouldBe(0.4340, 1e-4);
        trade.MaeR.ShouldBe(0.0943, 1e-4);
        trade.MfeR.ShouldBe(0.7736, 1e-4);
        exact.Walk.ExitsMaxHold.ShouldBe(1);
        var slipped = modelled.Trades.ShouldHaveSingleItem();
        slipped.ExitFilledPrice.ShouldBe(103.15, Tolerance);
        slipped.RFilled.ShouldBe(0.3962, 1e-4);
        slipped.SlippageR.ShouldBe(-0.0377, 1e-4);
    }

    [Fact]
    public void P4_a_gap_above_the_limit_is_a_counted_miss_and_is_not_retried()
    {
        var result = Go(Series(BaseDays.Flat(71, 102.40), BaseDays.Flat(72, 102.40)));

        result.Trades.ShouldBeEmpty();
        result.Walk.ShouldSatisfyAllConditions(
            w => w.Candidates.ShouldBe(1),
            w => w.LimitNotReached.ShouldBe(1),
            w => (w.Trades + w.NoFillBar + w.AfterSquareOff + w.EntryThroughStop + w.LimitNotReached + w.PositionOpen + w.OrderPendingAtEnd).ShouldBe(w.Candidates));
    }

    [Fact]
    public void P5_a_gap_through_the_base_is_entry_through_stop()
    {
        var result = Go(Series(BaseDays.Flat(71, 99.30)));

        result.Trades.ShouldBeEmpty();
        result.Walk.EntryThroughStop.ShouldBe(1);
        result.Walk.Candidates.ShouldBe(1);
    }

    [Fact]
    public void P6_a_volume_of_one_point_four_times_the_mean_gives_no_candidate_and_the_counter_of_the_volume_rule_has_no_true()
    {
        var result = Go([.. BaseDays.ThroughSignal(1.4), .. P1Tail()]);

        result.Outcomes.ShouldBeEmpty();
        var scan = result.Scans.Single();
        scan.Candidates.ShouldBe(0);
        var volume = scan.Conditions.Single(c => c.Path.Contains("setup[3]", StringComparison.Ordinal));
        volume.True.ShouldBe(0);
        volume.False.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(1.79, 0)]
    [InlineData(1.80, 1)]
    public void P7_the_base_tightness_boundary_is_one_point_seven_nine_six_percent(double basePct, int candidates)
    {
        var result = Go(Series(P1Tail()), basePct: basePct);

        result.Outcomes.Count.ShouldBe(candidates);
        var tightness = result.Scans.Single().Conditions.Single(c => c.Path.Contains("setup[2]", StringComparison.Ordinal));
        (tightness.True + tightness.False + tightness.Unknown + tightness.NotReady).ShouldBe(result.Scans.Single().WindowSteps);
        if (candidates == 0)
        {
            tightness.False.ShouldBeGreaterThan(0);
        }
    }

    [Theory]
    [InlineData(101.00, true)]
    [InlineData(100.99, false)]
    public void P8_a_close_equal_to_the_top_of_the_window_counts_and_a_cent_under_does_not(double close, bool candidate)
    {
        var result = Go([.. BaseDays.ThroughSignal(close: close), .. P1Tail()]);

        result.Outcomes.Count.ShouldBe(candidate ? 1 : 0);
        if (candidate)
        {
            result.Outcomes.Single().Candidate.StopLevel.ShouldBe(99.40, Tolerance);
        }
    }

    [Fact]
    public void P9a_a_daily_bar_that_closes_late_is_stale_and_the_signal_is_lost()
    {
        var late = Day.Dense(BaseDays.Date(70)).Start(100.70).Set("10:00 100.70/102.30/100.60/102.00").Absent("15:29").Volume(1_600);

        var result = Go([.. BaseDays.Ramp(), .. BaseDays.Base(), late, BaseDays.Flat(71, 102.00)]);

        result.Outcomes.ShouldBeEmpty();
        var scan = result.Scans.Single();
        scan.StaleBucketSteps.ShouldBe(1);
        scan.DecisionSteps.ShouldBe(scan.StaleBucketSteps + scan.OutOfWindowSteps + scan.WindowSteps);
    }

    [Fact]
    public void P9b_the_final_close_decides_not_the_earlier_running_close()
    {
        var falling = Day.Dense(BaseDays.Date(70)).Start(100.70).Set("10:00 100.70/102.30/100.60/102.00", "15:29 102.00/102.00/100.50/100.60").Volume(1_600);

        var result = Go([.. BaseDays.Ramp(), .. BaseDays.Base(), falling, BaseDays.Flat(71, 100.60)]);

        result.Outcomes.ShouldBeEmpty();
        result.Scans.Single().StaleBucketSteps.ShouldBe(0);
    }

    [Fact]
    public void P10_a_second_signal_inside_the_position_is_position_open_and_the_gate_releases_afterwards()
    {
        var tail = new[] { T(1, 102.05, 103.20, 101.80, 103.00, 1_600), T(2, 103.00, 105.00, 102.90, 104.80), T(3, 104.80, 107.50, 104.60, 107.00) };

        var result = Go(Series(tail));

        result.Walk.ShouldSatisfyAllConditions(
            w => w.Candidates.ShouldBe(2),
            w => w.Trades.ShouldBe(1),
            w => w.PositionOpen.ShouldBe(1));
        result.Scans.Single().Candidates.ShouldBe(2);
        Decided(result).ShouldBe([BaseDays.Date(70), BaseDays.Date(71)]);
        result.Outcomes[1].Candidate.StopLevel.ShouldBe(99.40, Tolerance);
    }

    [Fact]
    public void P10b_after_the_position_leaves_a_later_candidate_is_not_blocked()
    {
        var tail = new[]
        {
            T(1, 102.05, 103.20, 101.80, 103.00), T(2, 103.00, 105.00, 102.90, 104.80), T(3, 104.80, 107.50, 104.60, 107.00),
            T(4, 107.00, 108.00, 106.80, 107.50, 1_600), T(5, 107.60, 108.00, 107.40, 107.80),
        };

        var result = Go(Series(tail));

        result.Walk.Candidates.ShouldBe(2);
        result.Walk.PositionOpen.ShouldBe(0);
        result.Outcomes[1].Walk.Trade.ShouldNotBeNull().EntryPrice.ShouldBe(107.60, Tolerance);
    }
}
