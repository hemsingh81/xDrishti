using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.OvernightRows;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 1, 2 and 3 of P3-03e: the rows O1 to O9 (the ticket after a daily bar, E8 and E9 of ADR 0043) through the walker, with a hand-made candidate: the order rests overnight, fills at the next session's open within the limit, a gap above
/// the limit is a counted miss, the fill window is five minutes from the open, and a list that ends at the candidate's own minute is a pending order and not a miss. Every number is worked out by hand in the spec.
/// </summary>
public sealed class OvernightEntryTests
{
    private static readonly FillRules Modelled = FillRules.Default;

    private static readonly FillRules Exact = Cand.NoSlippage;

    [Fact]
    public void O1_modelled_the_order_fills_at_the_open_inside_the_limit_and_the_position_leaves_at_the_close_of_the_second_session()
    {
        var outcome = Walk(O1(), Modelled);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.TradeDate.ShouldBe(Tue);
        trade.DecisionEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:30")), "the decision stays on the decision date");
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("09:15")));
        (trade.EntryFill, trade.EntryPrice, Q(trade.EntryFilledPrice)).ShouldBe((EntryFill.AtOpen, 100.05, 100.10m), "modelled 100.125 up to 100.15, capped at the limit 100.10");
        (trade.StopPrice, trade.TargetPrice).ShouldBe((95.00, 110.15), "risk 5.05, target 100.05 + 2 x 5.05");
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:29")));
        (trade.ExitPrice, Q(trade.ExitFilledPrice)).ShouldBe((100.60, 100.50m), "slip 0.05 + 0.01 = 0.06: 100.54 down");
        (Q(trade.RExact), Q(trade.RFilled), Q(trade.SlippageR)).ShouldBe((0.1089m, 0.0792m, -0.0297m));
        (Q(trade.MaeR), Q(trade.MfeR)).ShouldBe((0.0297m, 0.1287m));
        (trade.SessionsHeld, trade.SessionsUnseen).ShouldBe((2, 0));
        outcome.BarsExamined.ShouldBe(750);
        outcome.EntryAfterMissingMinutes.ShouldBeFalse("the fill is at the open of the session");
        outcome.OvernightEntry.ShouldBeTrue();
        outcome.ToCounters().OvernightEntries.ShouldBe(1);
        trade.Key.ShouldBe("SWING_DAILY_TEST|TEST|Buy|2026-03-02", "the key follows the day of the decision");
    }

    [Fact]
    public void O1_exact_the_filled_prices_are_the_exact_ones_and_the_ratios_agree()
    {
        var trade = Walk(O1(), Exact).Trade.ShouldNotBeNull();

        (trade.EntryFilledPrice, trade.ExitFilledPrice).ShouldBe((trade.EntryPrice, trade.ExitPrice));
        trade.RFilled.ShouldBe(trade.RExact);
        trade.SlippageR.ShouldBe(0.0);
        (Q(trade.RExact), trade.TradeDate).ShouldBe((0.1089m, Tue));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void O2_an_open_beyond_the_limit_that_never_trades_back_to_it_is_a_counted_miss(bool modelled)
    {
        var bars = Day.Join(Monday(), Fill(Tue, "09:15 100.30/100.60/100.20/100.50"));

        var outcome = Walk(bars, modelled ? Modelled : Exact);

        outcome.Trade.ShouldBeNull();
        outcome.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
        outcome.BarsExamined.ShouldBe(1);
        outcome.ToCounters().LimitNotReached.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void O3_an_open_beyond_the_limit_whose_low_touches_it_fills_at_the_limit(bool modelled)
    {
        var bars = Day.Join(Monday(), Fill(Tue, "09:15 100.30/100.60/100.10/100.50"), Day.Dense(Wed).Start(100.50));

        var outcome = Walk(bars, modelled ? Modelled : Exact);

        var trade = outcome.Trade.ShouldNotBeNull();
        (trade.EntryFill, trade.EntryPrice, trade.EntryFilledPrice).ShouldBe((EntryFill.AtLimit, 100.10, 100.10), "a resting limit does not slip");
        Q(Math.Abs(trade.EntryPrice - trade.StopPrice)).ShouldBe(5.10m);
        trade.TradeDate.ShouldBe(Tue);
        outcome.ToCounters().OvernightEntries.ShouldBe(1);
    }

    [Fact]
    public void O3_the_favourable_range_of_the_fill_minute_is_not_used()
    {
        var bars = Day.Join(Monday(), Fill(Tue, "09:15 100.30/100.60/100.10/100.50"), Day.Dense(Wed).Start(100.50));

        var trade = Walk(bars, Exact).Trade.ShouldNotBeNull();

        trade.MfeR.ShouldBe((100.50 - 100.10) / 5.10, 1e-9, "X5: only the stop is checked in the fill minute of a limit entry, and the excursion starts from the entry, so the later close counts and the 100.60 high of the fill minute does not");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void O4_an_open_through_the_stop_is_entry_through_stop(bool modelled)
    {
        var bars = Day.Join(Monday(), Fill(Tue, "09:15 94.50/94.80/94.20/94.60"));

        var outcome = Walk(bars, modelled ? Modelled : Exact);

        outcome.NoTrade.ShouldBe(NoTradeReason.EntryThroughStop);
        outcome.BarsExamined.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_open_equal_to_the_limit_fills_at_the_open_and_an_open_equal_to_the_stop_is_through_the_stop(bool modelled)
    {
        var fills = modelled ? Modelled : Exact;

        var atLimit = Walk(Day.Join(Monday(), Fill(Tue, "09:15 100.10/100.40/100.10/100.30")), fills);
        var atStop = Walk(Day.Join(Monday(), Fill(Tue, "09:15 95.00/95.40/94.90/95.30")), fills);

        atLimit.Trade.ShouldNotBeNull().EntryFill.ShouldBe(EntryFill.AtOpen);
        atLimit.Trade!.EntryPrice.ShouldBe(100.10);
        atStop.NoTrade.ShouldBe(NoTradeReason.EntryThroughStop);
    }

    [Fact]
    public void O5_a_trading_day_with_no_bars_is_a_no_fill_bar_a_later_session_is_never_used()
    {
        var bars = Day.Join(Monday(), Day.Dense(Wed).Start(100.30).Set("09:15 100.05/100.40/99.90/100.30"));

        var outcome = Walk(bars, Modelled);

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        outcome.BarsExamined.ShouldBe(0);
    }

    [Fact]
    public void O5b_an_order_rests_over_a_holiday_and_fills_at_the_open_of_the_next_trading_day()
    {
        var bars = Day.Join(Monday(), Day.Dense(Wed).Start(100.30).Set("09:15 100.05/100.40/99.90/100.30"));

        var outcome = Walk(bars, Modelled, calendar: HolidayOnTuesday());

        var trade = outcome.Trade.ShouldNotBeNull();
        (trade.TradeDate, trade.EntryFill, trade.EntryPrice).ShouldBe((Wed, EntryFill.AtOpen, 100.05));
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("09:15")));
        outcome.ToCounters().OvernightEntries.ShouldBe(1, "O1, O3 and O5b count an overnight entry");
    }

    [Fact]
    public void O6_the_first_minute_that_holds_a_trade_at_0920_fills_and_at_0921_is_a_no_fill_bar()
    {
        var at0920 = Day.Join(Monday(), Day.Dense(Tue).Start(100.30).StartsAt("09:20").Set("09:20 100.05/100.40/99.90/100.30"));
        var at0921 = Day.Join(Monday(), Day.Dense(Tue).Start(100.30).StartsAt("09:21").Set("09:21 100.05/100.40/99.90/100.30"));

        var fills = Walk(at0920, Modelled);
        var misses = Walk(at0921, Modelled);

        fills.Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("09:20")));
        fills.EntryAfterMissingMinutes.ShouldBeTrue("a fill later than the open of the session");
        fills.ToCounters().EntriesAfterMissingMinutes.ShouldBe(1);
        misses.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Fact]
    public void O6_minutes_with_no_volume_at_the_open_are_passed_over_inside_the_five_minutes()
    {
        Day Tuesday(string lastEmpty)
        {
            var tuesday = Day.Dense(Tue).Start(100.30);
            for (var minute = Day.Minute("09:15"); minute <= Day.Minute(lastEmpty); minute++)
            {
                tuesday.SetEmpty(Day.Clock(minute) + " 100.30/100.30/100.30/100.30");
            }

            return tuesday.Set($"{Day.Clock(Day.Minute(lastEmpty) + 1)} 100.05/100.40/99.90/100.30");
        }

        var fills = Walk(Day.Join(Monday(), Tuesday("09:19")), Modelled);
        var misses = Walk(Day.Join(Monday(), Tuesday("09:20")), Modelled);

        fills.Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("09:20")));
        fills.ZeroVolumeMinutes.ShouldBe(5);
        misses.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        misses.ZeroVolumeMinutes.ShouldBe(6);
    }

    [Fact]
    public void O7_a_decision_on_a_friday_fills_on_the_monday_the_weekend_is_skipped()
    {
        var bars = Day.Join(Monday(Fri), Fill(NextMon), Day.Dense(NextMon.AddDays(1)).Start(100.30));

        var trade = Walk(bars, Modelled, Fri).Trade.ShouldNotBeNull();

        trade.TradeDate.ShouldBe(NextMon);
        trade.Key.ShouldBe("SWING_DAILY_TEST|TEST|Buy|2026-03-06");
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(NextMon, Day.Minute("09:15")));
    }

    [Fact]
    public void O7_a_saturday_with_minutes_is_not_the_fill_session_of_the_default_week_so_a_session_there_is_a_no_fill_bar()
    {
        var bars = Day.Join(Monday(Fri), Fill(Sat), Fill(NextMon));

        var outcome = Walk(bars, Modelled, Fri);

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar, "the next date in the list is not the fill session");
    }

    [Fact]
    public void O7_a_special_saturday_session_is_the_fill_session_when_the_calendar_has_it()
    {
        var bars = Day.Join(Monday(Fri), Day.Dense(Sat, "09:15", "12:29").Start(100.30).Set("09:15 100.05/100.40/99.90/100.30"));

        var trade = Walk(bars, Modelled, Fri, calendar: SpecialSaturday()).Trade.ShouldNotBeNull();

        trade.TradeDate.ShouldBe(Sat);
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Sat, Day.Minute("09:15")));
    }

    [Fact]
    public void O8_a_list_that_ends_at_the_candidates_own_minute_is_an_order_pending_at_the_end_not_a_miss()
    {
        var shortList = Day.Join(Monday());

        var outcome = Walk(shortList, Modelled);

        outcome.Trade.ShouldBeNull();
        outcome.NoTrade.ShouldBe(NoTradeReason.OrderPendingAtEnd);
        outcome.BarsExamined.ShouldBe(0);
        var counters = outcome.ToCounters();
        (counters.Candidates, counters.OrderPendingAtEnd, counters.NoFillBar, counters.Trades).ShouldBe((1L, 1L, 0L, 0L));
    }

    [Fact]
    public void O8_with_the_minutes_of_o1_added_the_candidate_is_the_trade_of_o1_dated_tuesday()
    {
        var longer = Walk(O1(), Modelled);
        var shorter = Walk(Day.Join(Monday()), Modelled);

        shorter.NoTrade.ShouldBe(NoTradeReason.OrderPendingAtEnd);
        longer.Trade.ShouldNotBeNull().TradeDate.ShouldBe(Tue);
    }

    [Fact]
    public void O8_a_list_that_ends_inside_the_fill_window_before_any_minute_holds_a_trade_is_pending_and_a_minute_more_may_fill_it()
    {
        Day Tuesday(string last)
        {
            var empty = Day.Dense(Tue, "09:15", last).Start(100.30);
            for (var minute = Day.Minute("09:15"); minute <= Day.Minute("09:17"); minute++)
            {
                empty.SetEmpty(Day.Clock(minute) + " 100.30/100.30/100.30/100.30");
            }

            return last == "09:18" ? empty.Set("09:18 100.05/100.40/99.90/100.30") : empty;
        }

        var shorter = Walk(Day.Join(Monday(), Tuesday("09:17")), Modelled);
        var longer = Walk(Day.Join(Monday(), Tuesday("09:18")), Modelled);

        shorter.NoTrade.ShouldBe(NoTradeReason.OrderPendingAtEnd, "the list ended while the order could still fill");
        shorter.ZeroVolumeMinutes.ShouldBe(3);
        longer.Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("09:18")));
    }

    [Fact]
    public void O8_a_list_that_runs_past_the_five_minutes_with_no_trade_is_a_no_fill_bar()
    {
        var empty = Day.Dense(Tue, "09:15", "09:22").Start(100.30);
        for (var minute = Day.Minute("09:15"); minute <= Day.Minute("09:22"); minute++)
        {
            empty.SetEmpty(Day.Clock(minute) + " 100.30/100.30/100.30/100.30");
        }

        Walk(Day.Join(Monday(), empty), Modelled).NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Fact]
    public void O9_a_position_that_leaves_at_mondays_close_does_not_block_the_candidate_decided_at_1530_the_order_fills_as_o1()
    {
        // Candidate A: decided Monday 10:15 (setup A like), a hold of 1 session: it leaves at Monday's 15:29 minute (hold end: the start of that minute). Candidate B: decided Monday 15:30.
        var monday = Monday().Set("10:15 100.00/100.00/100.00/100.00");
        var bars = Day.Join(monday, Fill(Tue), Day.Dense(Wed).Start(100.30));
        var a = Cand.Make(Side.Buy, bars.ToList().FindIndex(b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("10:14"))), "10:15", 95.00, "09:15", (100.00, 100.00, 100.00, 100.00), date: Mon);
        var b2 = Candidate(bars);

        var outcomes = PositionGate.Apply([a, b2], c => TradeWalker.Walk(c, bars, Rules(Modelled, sessions: 1))).Value;

        outcomes[0].Walk.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.MaxHold);
        outcomes[0].Walk.HoldEndEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:29")));
        outcomes[1].Walk.NoTrade.ShouldBeNull("D = 15:30 is after the hold end 15:29");
        var trade = outcomes[1].Walk.Trade.ShouldNotBeNull();
        (trade.TradeDate, trade.EntryFill, trade.EntryPrice).ShouldBe((Tue, EntryFill.AtOpen, 100.05));
    }

    [Fact]
    public void A_position_still_open_on_the_decision_day_blocks_the_candidate_decided_at_1530()
    {
        var monday = Monday().Set("10:15 100.00/100.00/100.00/100.00");
        var bars = Day.Join(monday, Fill(Tue), Day.Dense(Wed).Start(100.30));
        var a = Cand.Make(Side.Buy, bars.ToList().FindIndex(b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("10:14"))), "10:15", 95.00, "09:15", (100.00, 100.00, 100.00, 100.00), date: Mon);

        var outcomes = PositionGate.Apply([a, Candidate(bars)], c => TradeWalker.Walk(c, bars, Rules(Modelled, sessions: 2))).Value;

        outcomes[1].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen);
    }

    [Fact]
    public void An_order_that_rests_overnight_and_is_not_filled_does_not_set_the_gate_it_is_not_a_trade()
    {
        var bars = Day.Join(Monday(), Fill(Tue, "09:15 100.30/100.60/100.20/100.50"), Day.Dense(Wed).Start(100.50), Day.Dense(Thu).Start(100.50));
        var missed = Candidate(bars);
        var next = Candidate(bars, Tue, close: 100.50);

        var outcomes = PositionGate.Apply([missed, next], c => TradeWalker.Walk(c, bars, Rules(Modelled, sessions: 2))).Value;

        outcomes[0].Walk.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
        outcomes[1].Walk.NoTrade.ShouldNotBe(NoTradeReason.PositionOpen);
    }

    [Fact]
    public void An_intraday_walk_never_rests_overnight_a_decision_at_1530_is_a_no_fill_bar_with_or_without_the_next_day()
    {
        var withNextDay = Day.Join(Monday(), Fill(Tue));
        var candidate = Candidate(withNextDay);

        var longer = TradeWalker.Walk(candidate, withNextDay, Cand.Standard).Value;
        var ends = TradeWalker.Walk(Candidate(Day.Join(Monday())), Day.Join(Monday()), Cand.Standard).Value;

        longer.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        ends.NoTrade.ShouldBe(NoTradeReason.NoFillBar, "N3 of ADR 0038: the mirror case under an intraday walk stays a NoFillBar");
    }

    [Fact]
    public void A_swing_decision_inside_the_session_still_fills_on_its_own_date_and_a_decision_at_the_session_end_of_a_special_session_rests_overnight()
    {
        // A special Saturday session ends at 12:30: a decision at 12:30 rests overnight, a decision at 12:15 does not.
        var calendar = SpecialSaturday();
        var saturday = Day.Dense(Sat, "09:15", "12:29").Start(100.00);
        var bars = Day.Join(saturday, Day.Dense(NextMon).Start(100.00).Set("09:15 100.05/100.40/99.90/100.30"));
        var atEnd = Cand.Make(Side.Buy, bars.ToList().FindIndex(b => b.EpochSeconds == Day.Epoch(Sat, Day.Minute("12:29"))), "12:30", 95.00, "09:15", (100.00, 100.00, 100.00, 100.00), date: Sat);
        var inside = Cand.Make(Side.Buy, bars.ToList().FindIndex(b => b.EpochSeconds == Day.Epoch(Sat, Day.Minute("12:14"))), "12:15", 95.00, "09:15", (100.00, 100.00, 100.00, 100.00), date: Sat);

        var overnight = TradeWalker.Walk(atEnd, bars, Rules(Modelled, 2, calendar)).Value;
        var sameDay = TradeWalker.Walk(inside, bars, Rules(Modelled, 2, calendar)).Value;

        overnight.Trade.ShouldNotBeNull().TradeDate.ShouldBe(NextMon);
        sameDay.Trade.ShouldNotBeNull().TradeDate.ShouldBe(Sat);
        sameDay.OvernightEntry.ShouldBeFalse();
    }

    [Fact]
    public void A_list_that_ends_at_the_fill_minute_of_a_hold_of_one_session_leaves_with_the_data_after_one_minute()
    {
        var bars = Day.Join(Monday(), Day.Dense(Tue, "09:15", "09:15").Start(100.30).Set("09:15 100.05/100.40/99.90/100.30"));

        var outcome = Walk(bars, Modelled, sessions: 1);

        // The list ends at 09:15 on Tuesday, so the due minute 15:29 is not there: the position leaves with the data (X9), after one minute.
        outcome.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.EndOfData);
        outcome.BarsExamined.ShouldBe(1);
    }

    [Fact]
    public void A_zero_slippage_overnight_order_fills_at_the_exact_open()
    {
        var trade = Walk(O1(), Exact).Trade.ShouldNotBeNull();

        trade.EntryFilledPrice.ShouldBe(100.05);
    }

    [Fact]
    public void A_special_session_as_the_fill_session_opens_at_its_own_from_minute()
    {
        // A special Saturday session from 10:00 to 12:30: the fill window is the five minutes after 10:00, not after 09:15.
        var calendar = Of([], [(Sat, 10 * 60, (12 * 60) + 30)]);
        var onTime = Day.Join(Monday(Fri), Day.Dense(Sat, "10:00", "12:29").Start(100.30).Set("10:00 100.05/100.40/99.90/100.30"));
        var late = Day.Join(Monday(Fri), Day.Dense(Sat, "10:06", "12:29").Start(100.30).Set("10:06 100.05/100.40/99.90/100.30"));

        Walk(onTime, Modelled, Fri, calendar: calendar).Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Sat, Day.Minute("10:00")));
        Walk(late, Modelled, Fri, calendar: calendar).NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Fact]
    public void The_last_session_before_a_flagged_boundary_leaves_the_order_pending_never_a_trade_across_the_boundary()
    {
        // The replay hands the walker one segment: its list ends at the last session before the flagged day, so the order cannot be filled by a minute of the next segment.
        var segment = Day.Join(Monday());

        Walk(segment, Modelled).NoTrade.ShouldBe(NoTradeReason.OrderPendingAtEnd);
    }

    [Fact]
    public void The_walker_refuses_a_bar_list_that_is_not_ascending_in_the_overnight_scan()
    {
        var monday = Monday().Bars();
        var tuesday = Fill(Tue).Bars();
        var bars = new List<Bar>(monday) { tuesday[0], tuesday[2], tuesday[1] };

        var result = TradeWalker.Walk(Candidate(bars), bars, Rules(Modelled));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SimulationErrors.BarsNotAscendingCode);
    }
}
