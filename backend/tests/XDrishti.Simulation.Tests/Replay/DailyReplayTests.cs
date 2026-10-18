using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.Model;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criteria 2, 6, 9 and 10 of P3-03e through the real <see cref="InstrumentReplayer"/>: a daily strategy over 12 invented sessions (<see cref="DailyFixture"/>), the window cut by the day of the decision, the fates that add up, a 30m or 1h decision
/// at the close of the session, and the costing of a trade filled on the day after its decision.
/// </summary>
public sealed class DailyReplayTests
{
    private static readonly PreparedStrategy Daily = Rep.Prepare(TestStrategies.SwingDaily(DailyFixture.Trigger), Timeframe.D1, FillRules.Default);

    private static DateOnly S(int session) => DailyFixture.Dates[session - 1];

    private static InstrumentReplay Replay(IReadOnlyList<Bar> bars, DateOnly from, DateOnly to, CostContext? costs = null) =>
        Rep.Replay(Daily, bars, from, to, calendar: DefaultWeek(), costs: costs);

    [Fact]
    public void The_twelve_sessions_give_five_candidates_decided_at_1530_two_trades_a_counted_miss_a_position_open_and_a_pending_order()
    {
        var replay = Replay(DailyFixture.Twelve(), S(1), S(12));

        var fates = replay.Record.Fates;
        (fates.Trades, fates.OpenAtEnd, fates.LimitNotReached, fates.PositionOpen, fates.OrderPendingAtEnd).ShouldBe((2L, 0L, 1L, 1L, 1L));
        (fates.NoFillBar, fates.EntryThroughStop, fates.AfterSquareOff, fates.TradesBeforeWindow, fates.EntriesSuppressed, fates.ExcludedAcrossDiscontinuity).ShouldBe((0L, 0L, 0L, 0L, 0L, 0L));
        fates.Total.ShouldBe(5);
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates), "the fates add up to the candidates");
        replay.Record.Scans.Sum(s => s.Candidates).ShouldBe(5);
        replay.Record.Scans.Sum(s => s.StaleBucketSteps).ShouldBe(0);
    }

    [Fact]
    public void The_first_trade_enters_at_the_next_open_and_leaves_with_max_hold_at_the_close_of_the_fifth_session()
    {
        var replay = Replay(DailyFixture.Twelve(), S(1), S(12));

        var trade = replay.Trades[0];
        trade.DecisionEpochSeconds.ShouldBe(Day.Epoch(S(1), Day.Minute("15:30")));
        trade.TradeDate.ShouldBe(S(2));
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(S(2), Day.Minute("09:15")));
        (trade.EntryFill, trade.EntryPrice, trade.StopPrice).ShouldBe((EntryFill.AtOpen, 100.05, 98.00));
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(S(6), Day.Minute("15:29")));
        trade.ExitPrice.ShouldBe(100.50);
        (trade.SessionsHeld, trade.SessionsUnseen).ShouldBe((5, 0));
        trade.Key.ShouldBe("SWING_DAILY_TEST|TEST|Buy|2026-03-02", "the key follows the day of the decision");
    }

    [Fact]
    public void The_second_trade_is_stopped_at_the_open_of_the_next_monday_through_the_stop_an_overnight_gap_exit()
    {
        var replay = Replay(DailyFixture.Twelve(), S(1), S(12));

        var trade = replay.Trades[1];
        trade.DecisionEpochSeconds.ShouldBe(Day.Epoch(S(9), Day.Minute("15:30")));
        (trade.TradeDate, trade.EntryPrice, trade.StopPrice).ShouldBe((S(10), 104.05, 102.50));
        (trade.ExitReason, trade.ExitPrice).ShouldBe((ExitReason.Stop, 90.00));
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(S(11), Day.Minute("09:15")));
        trade.RExact.ShouldBe((90.00 - 104.05) / 1.55, 1e-9);
        var walk = replay.Record.Walk;
        (walk.OvernightStopGaps, walk.StopGapExits, walk.OvernightEntries, walk.ExitsMaxHold, walk.ExitsStop).ShouldBe((1L, 1L, 2L, 1L, 1L));
        replay.Record.Discontinuities.ShouldBeEmpty("13.5 % is under the 20 % threshold");
    }

    [Fact]
    public void The_walker_counters_hold_the_identities_of_the_pending_order_and_the_overnight_entries()
    {
        var walk = Replay(DailyFixture.Twelve(), S(1), S(12)).Record.Walk;

        (walk.Candidates, walk.Trades, walk.LimitNotReached, walk.PositionOpen, walk.OrderPendingAtEnd).ShouldBe((5L, 2L, 1L, 1L, 1L));
        (walk.Trades + walk.NoFillBar + walk.AfterSquareOff + walk.EntryThroughStop + walk.LimitNotReached + walk.PositionOpen + walk.OrderPendingAtEnd).ShouldBe(walk.Candidates);
        walk.OvernightEntries.ShouldBeLessThanOrEqualTo(walk.Trades);
        walk.NoFillBar.ShouldBe(0, "a pending order is never a NoFillBar");
    }

    [Fact]
    public void A_window_that_ends_on_the_last_decision_day_reports_the_pending_order_and_a_longer_window_reports_the_trade()
    {
        var shortRun = Replay(DailyFixture.Twelve(9), S(1), S(9));
        var longRun = Replay(DailyFixture.Twelve(10), S(1), S(10));

        shortRun.Record.Fates.OrderPendingAtEnd.ShouldBe(1, "C9 is decided at the close of the last session of the data");
        shortRun.Record.Fates.NoFillBar.ShouldBe(0);
        longRun.OpenAtEnd.ShouldHaveSingleItem().TradeDate.ShouldBe(S(10));
        longRun.Record.Fates.OrderPendingAtEnd.ShouldBe(0);
    }

    [Fact]
    public void A_window_with_the_data_of_one_day_gives_the_fate_and_two_days_give_the_trade_of_O1()
    {
        var monday = OvernightRows.Monday();
        var tuesday = OvernightRows.Fill(Tue);
        var strategy = Rep.Prepare(TestStrategies.SwingDaily(maxHoldSessions: 2), Timeframe.D1, FillRules.Default);

        var one = Rep.Replay(strategy, Day.Join(monday), Mon, Mon, calendar: DefaultWeek());
        var two = Rep.Replay(strategy, Day.Join(OvernightRows.Monday(), tuesday), Mon, Tue, calendar: DefaultWeek());

        (one.Record.Fates.OrderPendingAtEnd, one.Record.Fates.Total, one.Trades.Count + one.OpenAtEnd.Count).ShouldBe((1L, 1L, 0));
        var trade = two.OpenAtEnd.ShouldHaveSingleItem();
        (trade.TradeDate, trade.EntryPrice, trade.EntryFill).ShouldBe((Tue, 100.05, EntryFill.AtOpen));
        two.Record.Fates.OrderPendingAtEnd.ShouldBe(0);
    }

    [Fact]
    public void A_trade_is_cut_by_the_day_of_its_decision_a_candidate_decided_before_from_that_fills_on_from_is_a_trade_before_the_window()
    {
        // C1 is decided on S1 (Monday 2026-03-02) and filled on S2 (Tuesday 03-03): with From = S2 its trade is dated on From and is still before the window.
        var replay = Replay(DailyFixture.Twelve(), S(2), S(12));

        var fates = replay.Record.Fates;
        fates.TradesBeforeWindow.ShouldBe(1);
        fates.Trades.ShouldBe(1);
        replay.Record.CandidatesInWindow.ShouldBe(4, "C4, C7, C9 and C12; C1 was decided before From");
        replay.Record.TradesInWindow.ShouldBe(1);
        replay.Trades.ShouldHaveSingleItem().TradeDate.ShouldBe(S(10));
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
    }

    [Fact]
    public void A_candidate_decided_on_the_friday_before_from_that_fills_on_the_monday_that_is_from_is_a_trade_before_the_window()
    {
        var bars = Day.Join(OvernightRows.Monday(Fri), OvernightRows.Fill(NextMon), Day.Dense(NextMon.AddDays(1)).Start(100.30));
        var strategy = Rep.Prepare(TestStrategies.SwingDaily(maxHoldSessions: 2), Timeframe.D1, FillRules.Default);

        var replay = Rep.Replay(strategy, bars, NextMon, NextMon.AddDays(1), calendar: DefaultWeek());

        replay.Record.Fates.TradesBeforeWindow.ShouldBe(1);
        replay.Record.CandidatesInWindow.ShouldBe(2, "the candidates of Monday and Tuesday; the Friday one is before From");
        replay.Record.TradesInWindow.ShouldBeLessThanOrEqualTo(replay.Record.CandidatesInWindow);
    }

    [Fact]
    public void Trades_in_the_window_never_exceed_the_candidates_in_the_window_at_any_window_of_the_twelve_sessions()
    {
        var bars = DailyFixture.Twelve();
        var checkedWindows = 0;

        for (var from = 1; from <= 12; from++)
        {
            for (var to = from; to <= 12; to++)
            {
                var replay = Replay(DailyFixture.Twelve(to), S(from), S(to));

                replay.Record.TradesInWindow.ShouldBeLessThanOrEqualTo(replay.Record.CandidatesInWindow, $"[S{from}, S{to}]");
                replay.Record.Fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates), $"[S{from}, S{to}]");
                checkedWindows++;
            }
        }

        checkedWindows.ShouldBe(78);
        bars.Length.ShouldBe(12 * 375);
    }

    [Fact]
    public void A_trade_filled_on_the_day_after_its_decision_is_costed_with_the_delivery_segment_and_the_fill_date()
    {
        var context = TestCostSheet.Context(1_000_000m, 100m);

        var costed = Replay(DailyFixture.Twelve(), S(1), S(12), context);
        var gross = Replay(DailyFixture.Twelve(), S(1), S(12));

        costed.Trades.Count.ShouldBe(2);
        var trade = costed.Trades[0];
        var costing = trade.Costing.ShouldNotBeNull();
        costing.Segment.ShouldBe(CostSegment.Delivery, "a swing strategy holds overnight");
        trade.TradeDate.ShouldBe(S(2));
        var expectedEntryLeg = CostModel.Leg(context.Sheet, CostSegment.Delivery, Side.Buy, costing.EntryTurnover, S(2)).Value;
        costing.EntryLeg.ShouldBe(expectedEntryLeg, "the entry leg is charged on the date of the fill, the day after the decision");
        var expectedExitLeg = CostModel.Leg(context.Sheet, CostSegment.Delivery, Side.Sell, costing.ExitTurnover, S(6)).Value;
        costing.ExitLeg.ShouldBe(expectedExitLeg);
        costed.Record.Fates.Total.ShouldBe(gross.Record.Fates.Total);
        (costed.Record.Fates.OrderPendingAtEnd, costed.Record.Fates.PositionOpen).ShouldBe((gross.Record.Fates.OrderPendingAtEnd, gross.Record.Fates.PositionOpen));
        trade.Key.ShouldBe(gross.Trades[0].Key);
    }

    [Fact]
    public void A_trade_costed_into_a_skip_for_quantity_leaves_the_lists_and_is_counted_as_before_and_the_pending_order_stays_a_pending_order()
    {
        var context = TestCostSheet.Context(100m, 100m);

        var replay = Replay(DailyFixture.Twelve(), S(1), S(12), context);

        replay.Record.Fates.SkippedQuantity.ShouldBe(2);
        replay.Record.Fates.OrderPendingAtEnd.ShouldBe(1);
        replay.Record.Fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
    }

    [Fact]
    public void A_daily_candidate_on_the_last_session_before_a_flagged_day_is_pending_never_a_trade_across_the_boundary()
    {
        // Monday closes at 100.00, Tuesday opens at 60.00 (40 % under: flagged) and the walker of Monday's segment never sees Tuesday: the order is pending, and Tuesday's own candidate is dropped as suppressed.
        var bars = Day.Join(OvernightRows.Monday(), Day.Dense(Tue).Start(60.00));

        var replay = Rep.Replay(Rep.Prepare(TestStrategies.SwingDaily(maxHoldSessions: 2), Timeframe.D1, FillRules.Default), bars, Mon, Tue, calendar: DefaultWeek());

        var fates = replay.Record.Fates;
        (fates.OrderPendingAtEnd, fates.EntriesSuppressed, fates.Trades, fates.OpenAtEnd, fates.NoFillBar).ShouldBe((1L, 1L, 0L, 0L, 0L));
        fates.Total.ShouldBe(2);
        replay.Record.Discontinuities.Select(d => d.Date).ShouldBe([Tue]);
    }

    [Fact]
    public void A_30m_or_1h_decision_at_the_close_of_the_session_rests_overnight_and_fills_at_the_next_open()
    {
        // The last 1h bucket (15:15 to 15:30) closes at 101.50 above the high of the first hour (100.00); no last entry before 15:30, so the decision is at 15:30 and the order is for Tuesday's open.
        var strategy = TestStrategies.Swing1h() with { TimeRules = new TimeRules(new TimeOnly(10, 15), null, null, 3) };
        var monday = Day.Dense(Mon).Start(99.00).Set("09:20 99.00/100.00/99.00/99.00", "09:25 99.00/99.00/98.00/99.00", "15:20 99.50/101.50/99.50/101.50");
        var bars = Day.Join(monday, Day.After(monday, Tue), Day.After(Day.After(monday, Tue), Wed));

        var result = Sim.Run(strategy, bars, Timeframe.H1, calendar: DefaultWeek());

        var outcome = result.Outcomes.ShouldHaveSingleItem();
        outcome.Candidate.DecisionEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:30")));
        outcome.Candidate.TriggerBarMinutes.ShouldBe(15);
        var trade = outcome.Walk.Trade.ShouldNotBeNull();
        (trade.TradeDate, trade.EntryEpochSeconds, trade.EntryPrice, trade.EntryFill).ShouldBe((Tue, Day.Epoch(Tue, Day.Minute("09:15")), 101.50, EntryFill.AtOpen));
        result.Walk.OvernightEntries.ShouldBe(1);
        result.Walk.NoFillBar.ShouldBe(0);
    }

    [Fact]
    public void The_same_signal_under_an_intraday_strategy_is_a_no_fill_bar_it_never_rests_overnight()
    {
        var strategy = TestStrategies.StaleTest() with { TimeRules = new TimeRules(null, null, new TimeOnly(15, 15), null) };
        var monday = Day.Dense(Mon).Start(99.00).Set("15:20 99.50/101.50/99.50/101.50");
        var bars = Day.Join(monday, Day.After(monday, Tue));

        var result = Sim.Run(strategy, bars, Timeframe.M15, calendar: DefaultWeek());

        result.Walk.OvernightEntries.ShouldBe(0);
        result.Walk.OrderPendingAtEnd.ShouldBe(0);
        result.Outcomes.ShouldAllBe(o => o.Walk.NoTrade != NoTradeReason.OrderPendingAtEnd);
    }
}
