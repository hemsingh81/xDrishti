using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using static XDrishti.Simulation.Tests.Support.BaseRun;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// Criteria 9, 10, 13 and 14 of P3-06: the window rows on P1, the warm-up row P11, the ties of P12, the weekly rows W1 to W3 (pinning what the code gives, with a calendar and without one) and the costing of a swing trade in the delivery segment.
/// <para>
/// Observed answers of the weekly bar (D6): a weekly bar with a calendar closes at the step of the last session of the week, so on that day <c>tf("1W")</c> includes the decision day; without a calendar the bar is emitted at the first bar of the next week,
/// so a Friday decision sees the previous week.
/// </para>
/// </summary>
public sealed class BaseBreakoutWindowTests
{
    private static Day[] Through(int lastSession, params Day[] tail) => [.. BaseDays.ThroughSignal(), .. tail.Take(Math.Max(0, lastSession - 70))];

    private static DateOnly D(int session) => BaseDays.Date(session);

    [Theory]
    [InlineData(70, 0, 0, 1, 0)]
    [InlineData(71, 0, 1, 0, 0)]
    [InlineData(72, 0, 1, 0, 0)]
    [InlineData(73, 1, 0, 0, 0)]
    public void The_window_that_ends_at_to_reports_the_pending_order_the_open_position_or_the_trade(int to, int trades, int openAtEnd, int pending, int before)
    {
        var replay = Replayed(Through(to, P1Tail()), BaseDays.First, D(to));

        var fates = replay.Record.Fates;
        (fates.Trades, fates.OpenAtEnd, fates.OrderPendingAtEnd, fates.TradesBeforeWindow).ShouldBe(((long)trades, (long)openAtEnd, (long)pending, (long)before));
        fates.Total.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
    }

    [Fact]
    public void A_from_after_the_decision_makes_the_trade_a_trade_before_the_window_the_replay_still_runs_from_the_first_stored_bar()
    {
        var replay = Replayed(Through(73, P1Tail()), D(71), D(73));

        replay.Record.Fates.TradesBeforeWindow.ShouldBe(1);
        replay.Trades.ShouldBeEmpty();
    }

    [Fact]
    public void P1_through_the_replay_has_the_key_of_the_decision_date_and_the_trade_date_of_the_fill()
    {
        var trade = Replayed(Through(73, P1Tail()), BaseDays.First, D(73)).Trades.ShouldHaveSingleItem();

        trade.Key.ShouldBe("BASE_BREAKOUT|TEST|Buy|2026-04-13");
        trade.TradeDate.ShouldBe(new DateOnly(2026, 4, 14));
        (trade.ExitReason, trade.SessionsHeld).ShouldBe((ExitReason.Target, 3));
    }

    [Fact]
    public void P11_the_first_fully_ready_decision_is_the_fiftieth_session_and_forty_nine_sessions_are_skipped_as_features_not_ready()
    {
        var full = Replayed(Through(73, P1Tail()), BaseDays.First, D(73));
        var short49 = Replayed(BaseDays.Ramp().Take(49), BaseDays.First, D(48));

        var scan = full.Record.Scans.Single();
        scan.FullyReadySteps.ShouldBe(25, "74 sessions, the weekly bar of the last session of a week includes that session: the first fully ready decision is session 50, Friday 2026-03-13");
        scan.WindowSteps.ShouldBe(74);
        short49.Record.Skipped.ShouldBe(SkipReason.FeaturesNotReady);
        short49.Trades.ShouldBeEmpty();
    }

    [Fact]
    public void P12a_a_flat_series_ties_the_breakout_and_the_base_but_the_trend_gates_are_false_so_there_is_no_candidate()
    {
        var days = Enumerable.Range(0, 70).Select(i => BaseDays.Flat(i, 100.00, i == 69 ? 1_600 : 1_000)).ToArray();

        var result = Go(days);

        result.Outcomes.ShouldBeEmpty();
        var scan = result.Scans.Single();
        scan.Conditions.Single(c => c.Path.Contains("setup[0]", StringComparison.Ordinal)).True.ShouldBe(0);
        scan.Conditions.Single(c => c.Path.Contains("setup[1]", StringComparison.Ordinal)).True.ShouldBe(0);
        scan.Conditions.Single(c => c.Path.Contains("trigger[0]", StringComparison.Ordinal)).True.ShouldBeGreaterThan(0, "the tie counts");
    }

    [Fact]
    public void P12b_a_candidate_on_a_close_that_is_above_nothing_is_what_a_tie_costs()
    {
        var days = BaseDays.Ramp().Concat(Enumerable.Range(60, 10).Select(i => BaseDays.Flat(i, 99.75, i == 69 ? 1_600 : 1_000))).ToArray();

        var result = Go(days);

        Decided(result).ShouldBe([D(69)]);
        result.Outcomes.Single().Candidate.StopLevel.ShouldBe(99.75, 1e-9);
    }

    private static Day[] Flat100(int sessions) => [.. Enumerable.Range(0, sessions).Select(i => BaseDays.Flat(i, 100.00))];

    private static Day Pattern(int session) => BaseDays.Ohlc(session, 100.00, 100.90, 100.00, 100.90, 1_600);

    /// <summary>W1: the pattern on Friday 2026-04-03 (session 64).</summary>
    private static Day[] W1() => [.. Flat100(64), Pattern(64), BaseDays.Flat(65, 100.95)];

    /// <summary>W2: the pattern on Thursday 2026-04-02 (session 63), Friday flat at 100.90 with the usual volume.</summary>
    private static Day[] W2() => [.. Flat100(63), Pattern(63), BaseDays.Flat(64, 100.90), BaseDays.Flat(65, 100.95)];

    /// <summary>W3: the pattern on Thursday 2026-04-02 and a holiday on Friday 04-03.</summary>
    private static Day[] W3() => [.. Flat100(63), Pattern(63), BaseDays.Flat(65, 100.95)];

    [Fact]
    public void W1_with_a_calendar_the_week_of_the_decision_is_shown_on_its_last_session_so_friday_has_a_candidate()
    {
        Decided(Go(W1())).ShouldBe([D(64)]);
    }

    [Fact]
    public void W1_without_a_calendar_a_friday_decision_sees_the_previous_week_so_there_is_no_candidate()
    {
        Decided(Go(W1(), noCalendar: true)).ShouldBeEmpty();
    }

    [Fact]
    public void W2_the_week_in_progress_is_never_shown_so_the_same_pattern_one_session_earlier_has_no_candidate_on_either_day()
    {
        Decided(Go(W2())).ShouldBeEmpty();
        Decided(Go(W2(), noCalendar: true)).ShouldBeEmpty();
    }

    [Fact]
    public void W3_with_a_holiday_on_friday_the_week_ends_with_thursday_and_the_weekly_bar_closes_at_its_step()
    {
        Decided(Go(W3(), calendar: Rep.WithHoliday(D(64)))).ShouldBe([D(63)]);
    }

    [Fact]
    public void W3_without_a_calendar_thursday_is_not_known_to_end_the_week()
    {
        Decided(Go(W3(), noCalendar: true)).ShouldBeEmpty();
    }

    [Fact]
    public void A_swing_trade_is_costed_in_the_delivery_segment_with_the_dp_charge_on_the_sell_leg_only_and_a_gross_trade_has_no_costing()
    {
        var days = Through(73, P1Tail());
        var strategy = Rep.Prepare(BaseBreakoutV1.Create(BaseRun.BaseLen), XDrishti.Domain.Market.Bars.Timeframe.D1, FillRules.Default);
        var context = TestCostSheet.Context(1_000_000m, 100m);

        var costed = Rep.Replay(strategy, BaseDays.Bars(days), BaseDays.First, D(73), calendar: Rep.DefaultCalendar, costs: context);
        var gross = Rep.Replay(strategy, BaseDays.Bars(days), BaseDays.First, D(73), calendar: Rep.DefaultCalendar);

        var trade = costed.Trades.ShouldHaveSingleItem();
        var costing = trade.Costing.ShouldNotBeNull();
        costing.Segment.ShouldBe(CostSegment.Delivery);
        costing.EntryLeg.DpCharge.ShouldBe(0m);
        costing.ExitLeg.DpCharge.ShouldBeGreaterThan(0m);
        costing.EntryLeg.ShouldBe(CostModel.Leg(context.Sheet, CostSegment.Delivery, Side.Buy, costing.EntryTurnover, trade.TradeDate).Value);
        costing.ExitLeg.ShouldBe(CostModel.Leg(context.Sheet, CostSegment.Delivery, Side.Sell, costing.ExitTurnover, D(73)).Value);
        costing.RNet.ShouldBe(trade.RFilled - costing.CostR, 1e-9);
        gross.Trades.ShouldHaveSingleItem().Costing.ShouldBeNull();
    }

    [Fact]
    public void A_calendar_with_a_holiday_in_the_hold_moves_the_fifth_session_after_it()
    {
        // the holiday is Wednesday 2026-04-15 (T2): the five sessions of the hold are T1, T3, T4, T5, T6
        var calendar = Rep.WithHoliday(D(72));
        var tail = new[]
        {
            T(1, 102.05, 103.20, 101.80, 103.00), T(3, 103.00, 103.40, 102.20, 102.80), T(4, 102.80, 104.00, 102.40, 102.70), T(5, 102.70, 103.60, 102.30, 103.50), T(6, 103.50, 104.10, 102.90, 103.20),
        };

        var result = Go([.. BaseDays.ThroughSignal(), .. tail], calendar: calendar);

        var trade = result.Outcomes.Single().Walk.Trade.ShouldNotBeNull();
        (trade.ExitReason, trade.SessionsHeld).ShouldBe((ExitReason.MaxHold, 5));
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(D(76), Day.Minute("15:29")));
    }

    [Fact]
    public void An_instrument_with_exactly_fifty_sessions_has_one_fully_ready_decision_on_the_fiftieth()
    {
        var days = new List<Day>(Flat100(49)) { Pattern(49) };

        var result = Go(days);

        Decided(result).ShouldBe([D(49)]);
        result.Scans.Single().FullyReadySteps.ShouldBe(1);
    }

    [Fact]
    public void A_base_of_sixty_sessions_with_fifty_nine_sessions_is_skipped_as_features_not_ready()
    {
        var strategy = Rep.Prepare(BaseBreakoutV1.Create(60), XDrishti.Domain.Market.Bars.Timeframe.D1, Cand.NoSlippage);
        var days = BaseDays.Ramp().Take(59);

        var replay = Rep.Replay(strategy, BaseDays.Bars(days), BaseDays.First, D(58), calendar: Rep.DefaultCalendar);

        replay.Record.Skipped.ShouldBe(SkipReason.FeaturesNotReady);
    }

    [Fact]
    public void A_signal_on_the_last_session_of_a_week_rests_over_the_weekend_and_fills_at_the_next_monday_open()
    {
        var result = Go(W1(), exact: true);

        var trade = result.Outcomes.Single().Walk.Trade.ShouldNotBeNull();
        (trade.TradeDate, trade.EntryFill, trade.EntryPrice).ShouldBe((D(65), EntryFill.AtOpen, 100.95));
        trade.Key.ShouldEndWith("|" + D(64).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_split_inside_the_base_leaves_the_adjusted_series_continuous_and_gives_the_trade_of_row_P1()
    {
        var exDate = D(66);
        var plain = BaseDays.Bars([.. BaseDays.ThroughSignal(), .. P1Tail()]);
        var raw = plain.Select(b => SessionClock.DateOf(b.EpochSeconds) < exDate ? b with { Open = b.Open * 2, High = b.High * 2, Low = b.Low * 2, Close = b.Close * 2, Volume = b.Volume / 2 } : b).ToArray();
        var strategy = Rep.Prepare(BaseBreakoutV1.Create(BaseRun.BaseLen), XDrishti.Domain.Market.Bars.Timeframe.D1, Cand.NoSlippage);

        var split = Rep.Replay(strategy, raw, BaseDays.First, D(73), factors: Rep.Split(exDate, 1, 2), calendar: Rep.DefaultCalendar);
        var direct = Rep.Replay(strategy, plain, BaseDays.First, D(73), calendar: Rep.DefaultCalendar);

        var trade = split.Trades.ShouldHaveSingleItem();
        (trade.EntryPrice, trade.StopPrice, trade.ExitPrice, trade.ExitReason).ShouldBe((102.05, 99.40, 107.35, ExitReason.Target));
        trade.ShouldBe(direct.Trades.Single());
    }
}
