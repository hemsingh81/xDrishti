using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 1, 2, 4, 6, 7 and 9 of P3-03d: the hand-calculated swing trades M1 to M7b (setup A, the doc 16 slippage), the target that gives no improvement overnight, the exit at the close of the due session and at the last known
/// close, the end of the data and the unseen sessions. Prices are compared as decimals rounded to 4 places; R, MAE and MFE are the quotients of the prices in decimal (the convention of ADR 0038).
/// </summary>
public sealed class SwingWalkTests
{
    public static TheoryData<string> Names => [.. SwingCases.All.Select(c => c.Hand.Name)];

    private static decimal Q(double value) => Math.Round((decimal)value, 4);

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_exit_price_reason_ratio_session_count_and_counter_of_the_row_equals_the_table(string name)
    {
        var (hand, want) = (SwingCases.Get(name).Hand, SwingCases.Get(name).Want);

        var outcome = hand.Walk();

        var trade = outcome.Trade.ShouldNotBeNull(name);
        Q(trade.ExitPrice).ShouldBe(want.Exit, name + " exit");
        Q(trade.ExitFilledPrice).ShouldBe(want.ExitFilled, name + " exit filled");
        trade.ExitReason.ShouldBe(want.Reason, name);
        Q(trade.RExact).ShouldBe(want.RExact, name + " RExact");
        Q(trade.RFilled).ShouldBe(want.RFilled, name + " RFilled");
        Q(trade.SlippageR).ShouldBe(want.SlippageR, name + " SlippageR");
        Q(trade.MaeR).ShouldBe(want.Mae, name + " MAE");
        Q(trade.MfeR).ShouldBe(want.Mfe, name + " MFE");
        (trade.SessionsHeld, trade.SessionsUnseen).ShouldBe((want.Held, want.Unseen), name);
        outcome.BarsExamined.ShouldBe(want.Examined, name + " BarsExamined");
        trade.SchemaVersion.ShouldBe(3, name);
        trade.Fills.ShouldBe(FillBasis.Modelled, name);
        Q(trade.EntryPrice).ShouldBe(100.00m, name);
        Q(trade.EntryFilledPrice).ShouldBe(100.10m, name);
        trade.StopPrice.ShouldBe(99.00, name + ": the stop is the same level on every day");
        trade.TargetPrice.ShouldBe(102.00, name + ": the target is the same level on every day");

        var counters = outcome.ToCounters();
        (outcome.StopGapExit, outcome.OvernightStopGap, outcome.MaxHoldAtLastClose).ShouldBe((want.StopGap, want.OvernightStopGap, want.MaxHoldAtLastClose), name);
        (outcome.TargetGapExit, outcome.OvernightTargetGap).ShouldBe((want.TargetGap, want.OvernightTargetGap), name);
        counters.ExitsStop.ShouldBe(want.Exits == "stop" ? 1 : 0, name);
        counters.ExitsTarget.ShouldBe(want.Exits == "target" ? 1 : 0, name);
        counters.ExitsMaxHold.ShouldBe(want.Exits == "maxhold" ? 1 : 0, name);
        counters.ExitsEndOfData.ShouldBe(want.Exits == "endofdata" ? 1 : 0, name);
        counters.ExitsSquareOff.ShouldBe(0, name);
        counters.SessionsHeld.ShouldBe(want.Held, name);
        counters.SessionsUnseen.ShouldBe(want.Unseen, name);
        counters.MaxHoldAtLastClose.ShouldBe(want.MaxHoldAtLastClose ? 1 : 0, name);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void With_no_slippage_the_filled_layer_is_the_exact_one_on_every_row(string name)
    {
        var (hand, want) = (SwingCases.Get(name).Hand, SwingCases.Get(name).Want);

        var trade = hand.Walk(Cand.NoSlippage).Trade.ShouldNotBeNull(name);

        trade.ExitFilledPrice.ShouldBe(trade.ExitPrice, name);
        trade.EntryFilledPrice.ShouldBe(trade.EntryPrice, name);
        trade.RFilled.ShouldBe(trade.RExact, name);
        trade.SlippageR.ShouldBe(0, name);
        Q(trade.ExitPrice).ShouldBe(want.Exit, name);
        trade.ExitReason.ShouldBe(want.Reason, name);
        Q(trade.RExact).ShouldBe(want.RExact, name);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_ratios_are_the_quotients_of_the_prices_and_the_excursions_bracket_the_result(string name)
    {
        var trade = SwingCases.Get(name).Hand.Walk().Trade.ShouldNotBeNull(name);

        var risk = Math.Abs((decimal)trade.EntryPrice - (decimal)trade.StopPrice);
        ((double)(((decimal)trade.ExitPrice - (decimal)trade.EntryPrice) / risk)).ShouldBe(trade.RExact, 1e-9, name);
        ((double)(((decimal)trade.ExitFilledPrice - (decimal)trade.EntryFilledPrice) / risk)).ShouldBe(trade.RFilled, 1e-9, name);
        (-trade.MaeR).ShouldBeLessThanOrEqualTo(trade.RExact + 1e-12, name);
        trade.RExact.ShouldBeLessThanOrEqualTo(trade.MfeR + 1e-12, name);
        trade.SlippageR.ShouldBeLessThanOrEqualTo(0, name);
    }

    [Fact]
    public void A_stop_that_the_market_opens_through_exits_at_the_open_and_a_low_above_the_stop_does_not_exit_M1_and_M2()
    {
        var m1 = SwingCases.Get("M1").Hand.Walk();
        var m2 = SwingCases.Get("M2").Hand.Walk();

        m1.Trade!.ExitEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("09:15")));
        m1.Trade.ExitPrice.ShouldBe(98.60);
        m1.ToCounters().OvernightStopGaps.ShouldBe(1);
        m2.ToCounters().OvernightStopGaps.ShouldBe(0);
        m2.Trade!.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("10:40")));
        m2.Trade.MaeR.ShouldBe(0.9, 1e-9);
    }

    [Fact]
    public void A_target_that_the_market_opens_beyond_overnight_still_exits_at_the_target_and_is_counted_H5_on_the_second_day()
    {
        var hand = SwingHand.A("target-gap", 5).Then(Tue, "09:15 103.20/103.50/103.00/103.40");

        var outcome = hand.Walk();

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.ExitPrice.ShouldBe(102.00);
        Q(trade.RExact).ShouldBe(2.0000m);
        Q(trade.RFilled).ShouldBe(1.9000m);
        Q(trade.MfeR).ShouldBe(2.0000m);
        Q(trade.MaeR).ShouldBe(0.2000m);
        outcome.TargetGapExit.ShouldBeTrue();
        outcome.OvernightTargetGap.ShouldBeTrue();
        outcome.ToCounters().TargetGapExits.ShouldBe(1);
        outcome.ToCounters().OvernightTargetGaps.ShouldBe(1);
        (trade.SessionsHeld, trade.SessionsUnseen).ShouldBe((2, 0));
    }

    [Fact]
    public void An_open_exactly_at_the_stop_exits_at_the_open_and_is_not_a_gap_exit_and_an_open_exactly_at_the_target_exits_at_the_target()
    {
        var atStop = SwingHand.A("open-at-stop", 5).Then(Tue, "09:15 99.00/99.20/98.90/99.10").Walk();
        var atTarget = SwingHand.A("open-at-target", 5).Then(Tue, "09:15 102.00/102.10/101.90/102.00").Walk();

        atStop.Trade!.ExitReason.ShouldBe(ExitReason.Stop);
        atStop.Trade.ExitPrice.ShouldBe(99.00);
        atStop.StopGapExit.ShouldBeFalse();
        atStop.OvernightStopGap.ShouldBeFalse();
        atTarget.Trade!.ExitReason.ShouldBe(ExitReason.Target);
        atTarget.Trade.ExitPrice.ShouldBe(102.00);
        atTarget.TargetGapExit.ShouldBeFalse();
        atTarget.OvernightTargetGap.ShouldBeFalse();
    }

    [Fact]
    public void An_overnight_open_beyond_the_target_with_the_low_through_the_stop_is_the_target_the_first_print_decides()
    {
        var outcome = SwingHand.A("both", 5).Then(Tue, "09:15 103.00/103.30/98.90/100.00").Walk();

        outcome.Trade!.ExitReason.ShouldBe(ExitReason.Target);
        outcome.Trade.ExitPrice.ShouldBe(102.00);
        outcome.OvernightTargetGap.ShouldBeTrue();
        outcome.AmbiguousMinute.ShouldBeFalse();
    }

    [Fact]
    public void A_stop_and_a_target_in_one_later_minute_with_the_open_between_them_is_the_stop_and_ambiguous()
    {
        var outcome = SwingHand.A("ambiguous-later", 5).Then(Tue, "11:00 100.50/102.40/98.70/100.00").Walk();

        outcome.Trade!.ExitReason.ShouldBe(ExitReason.Stop);
        outcome.Trade.ExitPrice.ShouldBe(99.00);
        outcome.AmbiguousMinute.ShouldBeTrue();
        outcome.OvernightStopGap.ShouldBeFalse();
    }

    [Fact]
    public void A_stop_touched_in_the_first_minute_of_a_later_day_without_a_gap_exits_at_the_stop_and_is_overnight_but_not_a_gap()
    {
        // The open 99.50 is above the stop 99.00 and the low 98.90 touches it: a stop at the level, not a gap exit, so neither counter moves.
        var outcome = SwingHand.A("first-minute", 5).Then(Tue, "09:15 99.50/99.60/98.90/99.20").Walk();

        outcome.Trade!.ExitReason.ShouldBe(ExitReason.Stop);
        outcome.Trade.ExitPrice.ShouldBe(99.00);
        outcome.StopGapExit.ShouldBeFalse();
        outcome.OvernightStopGap.ShouldBeFalse();
    }

    [Fact]
    public void The_exit_instant_of_a_max_hold_is_the_start_of_the_due_minute_and_the_stop_comes_first_in_that_minute_M3_and_M6()
    {
        var m3 = SwingCases.Get("M3").Hand.Walk().Trade!;
        var m6 = SwingCases.Get("M6").Hand.Walk().Trade!;
        var m3c = SwingCases.Get("M3c").Hand.Walk().Trade!;

        m3.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:29")));
        m3c.ExitEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:29")), "N = 1 leaves at the close of the entry day, not at a 15:15 square-off");
        m6.ExitReason.ShouldBe(ExitReason.Stop);
        m6.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:29")));
    }

    [Fact]
    public void A_list_that_ends_at_the_due_minute_with_a_minute_that_holds_a_trade_is_a_max_hold_and_with_an_empty_minute_it_is_the_end_of_the_data_M3_and_M7b()
    {
        SwingCases.Get("M3").Hand.Walk().Trade!.ExitReason.ShouldBe(ExitReason.MaxHold);
        SwingCases.Get("M7b").Hand.Walk().Trade!.ExitReason.ShouldBe(ExitReason.EndOfData);
        SwingCases.Get("M7a").Hand.Walk().Trade!.ExitReason.ShouldBe(ExitReason.MaxHold);
    }

    [Fact]
    public void The_hold_end_of_a_trade_is_its_exit_instant_except_after_a_max_hold_at_the_last_close_where_it_is_the_end_of_the_due_session()
    {
        var m3 = SwingCases.Get("M3").Hand.Walk();
        var m7a = SwingCases.Get("M7a").Hand.Walk();
        var m7b = SwingCases.Get("M7b").Hand.Walk();

        m3.HoldEndEpochSeconds.ShouldBe(m3.Trade!.ExitEpochSeconds);
        m7b.HoldEndEpochSeconds.ShouldBe(m7b.Trade!.ExitEpochSeconds);
        m7a.Trade!.ExitEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:28")));
        m7a.HoldEndEpochSeconds.ShouldBe(Day.Epoch(Wed, Day.Minute("15:30")), "the position counts as open until the session in which it was due ends");
    }

    [Fact]
    public void A_session_with_no_minute_at_all_inside_the_hold_is_counted_as_unseen_and_the_trade_is_kept()
    {
        // M3 with Tuesday absent from the list while the default week says it is a session: Wednesday is session 3 and the due session.
        var hand = SwingHand.A("tuesday-missing", 3).Then(Wed, "15:29 100.60/100.80/100.50/100.70");

        var outcome = hand.Walk();

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.MaxHold);
        (trade.SessionsHeld, trade.SessionsUnseen).ShouldBe((3, 1));
        outcome.ToCounters().SessionsUnseen.ShouldBe(1);
    }

    [Fact]
    public void A_halt_that_leaves_a_day_with_empty_minutes_only_is_an_unseen_session_and_the_reopening_gap_fills_at_the_open()
    {
        // Tuesday is listed but every minute has volume 0 (a halt); Wednesday opens through the stop.
        var tuesday = Day.Dense(Tue).Start(100.20);
        for (var minute = SessionClock.OpenMinute; minute < SessionClock.CloseMinute; minute++)
        {
            tuesday.SetEmpty(Day.Clock(minute) + " 100.20/100.20/100.20/100.20");
        }

        var hand = SwingHand.A("halt", 5);
        var bars = hand.Bars.Concat(tuesday.Bars()).Concat(Day.Dense(Wed).Start(100.20).Set("09:15 98.60/98.90/98.40/98.50").Bars()).ToArray();
        var result = TradeWalker.Walk(hand.Candidate(), bars, hand.Rules(FillRules.Default));

        var outcome = result.Value;
        outcome.Trade!.ExitReason.ShouldBe(ExitReason.Stop);
        outcome.Trade.ExitPrice.ShouldBe(98.60);
        (outcome.Trade.SessionsHeld, outcome.Trade.SessionsUnseen).ShouldBe((3, 1));
        outcome.ZeroVolumeMinutes.ShouldBe(375);
        outcome.OvernightStopGap.ShouldBeTrue();
    }

    [Fact]
    public void A_swing_walk_has_no_square_off_the_trade_goes_through_15_15_and_15_30_on_every_date()
    {
        var m3 = SwingCases.Get("M3").Hand.Walk();

        m3.Trade!.ExitReason.ShouldNotBe(ExitReason.SquareOff);
        m3.LateSquareOff.ShouldBeFalse();
        m3.ToCounters().ExitsSquareOff.ShouldBe(0);
        m3.ToCounters().AfterSquareOff.ShouldBe(0);
    }

    [Fact]
    public void An_entry_in_the_last_minute_of_the_day_is_the_due_minute_of_a_one_session_hold()
    {
        // The decision at 15:29 fills in 15:29's next minute: there is none, so the fill is at 15:29 only when the decision is one minute earlier. Decision 15:29 -> fill minute 15:29 is the due minute.
        var bars = Day.Dense(Mon).Start(100.00).Set("15:29 100.00/100.20/99.90/100.10").Bars();
        var candidate = Cand.Make(Side.Buy, Array.FindIndex(bars, b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("15:28"))), "15:29", 99.00, "14:30", (100.00, 100.00, 100.00, 100.00), date: Mon);
        var rules = new WalkRules(2.0, WalkRules.NoSquareOff, FillRules.Default, TickSpec.FromRupees(0.05m).Value, SwingHold.Create(1, null).Value);

        var outcome = TradeWalker.Walk(candidate, bars, rules).Value;

        outcome.Trade!.ExitReason.ShouldBe(ExitReason.MaxHold);
        outcome.Trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:29")));
        outcome.Trade.ExitEpochSeconds.ShouldBe(Day.Epoch(Mon, Day.Minute("15:29")));
        outcome.BarsExamined.ShouldBe(1);
    }

    [Fact]
    public void A_decision_at_the_close_of_the_session_is_a_no_fill_bar_in_a_swing_walk_too()
    {
        // 15:30: the next minute is on another date (D1, P3-03e's overnight entry order is not built here).
        var hand = SwingHand.A("close-decision", 3).Flat(Tue);
        var bars = hand.Bars;
        var own = Array.FindIndex([.. bars], b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("15:29")));
        var candidate = Cand.Make(Side.Buy, own, "15:30", 99.00, "14:30", (100.00, 100.00, 100.00, 100.00), date: Mon);

        var outcome = TradeWalker.Walk(candidate, bars, hand.Rules(FillRules.Default)).Value;

        outcome.Trade.ShouldBeNull();
        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Fact]
    public void The_entry_rules_are_those_of_the_intraday_walk_a_limit_not_reached_is_final_in_a_swing_walk()
    {
        // The fill minute opens 101.00, above the limit 100.10, and never trades back to it.
        var bars = Day.Dense(Mon).Start(100.00).Set("10:00 101.00/101.20/100.50/101.10").Bars();
        var candidate = Cand.Make(Side.Buy, Array.FindIndex(bars, b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("09:59"))), "10:00", 99.00, "09:45", (100.00, 100.00, 100.00, 100.00), date: Mon);
        var rules = new WalkRules(2.0, WalkRules.NoSquareOff, FillRules.Default, TickSpec.FromRupees(0.05m).Value, SwingHold.Create(3, null).Value);

        var outcome = TradeWalker.Walk(candidate, bars, rules).Value;

        outcome.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
    }

    [Fact]
    public void In_the_fill_minute_of_an_entry_at_the_limit_a_high_beyond_the_target_is_not_a_target_X5()
    {
        // Fill minute 10:00 103.00/103.20/100.00/100.40: opens above the limit 100.10, trades back to it (AtLimit), high 103.20 is beyond the target 102.30 but may have come before the fill.
        var bars = Day.Dense(Mon).Start(100.00).Set("10:00 103.00/103.20/100.00/100.40").Bars();
        var candidate = Cand.Make(Side.Buy, Array.FindIndex(bars, b => b.EpochSeconds == Day.Epoch(Mon, Day.Minute("09:59"))), "10:00", 99.00, "09:45", (100.00, 100.00, 100.00, 100.00), date: Mon);
        var rules = new WalkRules(2.0, WalkRules.NoSquareOff, FillRules.Default, TickSpec.FromRupees(0.05m).Value, SwingHold.Create(1, null).Value);

        var outcome = TradeWalker.Walk(candidate, bars, rules).Value;

        outcome.Trade!.EntryFill.ShouldBe(EntryFill.AtLimit);
        outcome.Trade.ExitReason.ShouldBe(ExitReason.MaxHold);
    }

    [Fact]
    public void Slippage_of_a_max_hold_and_an_end_of_data_exit_has_no_extra_ticks_and_a_stop_has_them_and_a_target_none_S5()
    {
        var maxHold = SwingCases.Get("M3").Hand.Walk().Trade!;
        var endOfData = SwingCases.Get("M5").Hand.Walk().Trade!;
        var stop = SwingCases.Get("M6").Hand.Walk().Trade!;
        var target = SwingCases.Get("M2").Hand.Walk().Trade!;

        // (1 tick * 0.05) + 0.05 * range of the exit minute, rounded down for a long.
        Q(maxHold.ExitPrice - maxHold.ExitFilledPrice).ShouldBe(0.10m);   // 100.70 -> 100.60 (amount 0.065)
        Q(endOfData.ExitPrice - endOfData.ExitFilledPrice).ShouldBe(0.05m);   // flat minute: the tick only
        Q(stop.ExitPrice - stop.ExitFilledPrice).ShouldBe(0.25m);   // 99.00 -> 98.75 (amount 0.245: 3 ticks and 5 % of 1.90)
        target.ExitFilledPrice.ShouldBe(target.ExitPrice);
    }
}
