using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// The entry of P3-03b (rows E0 to E7): the limit on the grid (criterion 1), the nine boundaries B1 to B9 (2), a miss is final (3), the fill minute with its holes and empty minutes (4), a fill through the stop (5) and the walker's
/// refusal of rules that bypassed their factories (11).
/// </summary>
public sealed class EntryRulesTests
{
    private static string Row(string time, double open, double high, double low, double close) =>
        string.Create(CultureInfo.InvariantCulture, $"{time} {open}/{high}/{low}/{close}");

    private static Hand Boundary(string name, Side side, double open, double high, double low) =>
        (side == Side.Buy ? Hand.Buy(name, 2950.00, 2921.50) : Hand.Sell(name, 2950.00, 2978.50)).Rows(Row("10:00", open, high, low, open));

    /// <summary>The ticket after doc 07 §2: invented trigger close 2,950.00; BUY limit 2,952.95 and stop 2,921.50; SELL limit 2,947.05 and stop 2,978.50; fill minute 10:00.</summary>
    public static TheoryData<string, Side, double, double, double, string, double> Boundaries => new()
    {
        { "B1", Side.Buy, 2951.00, 2954.00, 2950.50, "AtOpen", 2951.00 },
        { "B2", Side.Buy, 2952.95, 2953.50, 2952.40, "AtOpen", 2952.95 },
        { "B3", Side.Buy, 2953.00, 2953.60, 2952.95, "AtLimit", 2952.95 },
        { "B4", Side.Buy, 2953.00, 2953.60, 2953.00, "LimitNotReached", 0 },
        { "B5", Side.Buy, 2921.50, 2922.00, 2920.00, "EntryThroughStop", 0 },
        { "B6", Side.Sell, 2949.00, 2949.40, 2948.10, "AtOpen", 2949.00 },
        { "B7", Side.Sell, 2947.05, 2947.40, 2946.60, "AtOpen", 2947.05 },
        { "B8", Side.Sell, 2947.00, 2947.05, 2946.60, "AtLimit", 2947.05 },
        { "B9", Side.Sell, 2947.00, 2947.00, 2946.50, "LimitNotReached", 0 },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void An_open_equal_to_the_limit_is_at_the_open_a_low_or_high_equal_to_it_is_at_the_limit_and_one_tick_short_is_a_miss(
        string name, Side side, double open, double high, double low, string result, double entry)
    {
        var outcome = Boundary(name, side, open, high, low).Walk(FillRules.Default);

        if (result is "AtOpen" or "AtLimit")
        {
            var trade = outcome.Trade.ShouldNotBeNull(name);
            trade.EntryFill.ShouldBe(Enum.Parse<EntryFill>(result), name);
            trade.EntryPrice.ShouldBe(entry, name);
            trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:00")), name);
        }
        else
        {
            outcome.Trade.ShouldBeNull(name);
            outcome.NoTrade.ShouldBe(Enum.Parse<NoTradeReason>(result), name);
            outcome.BarsExamined.ShouldBe(1, name);
        }
    }

    [Theory]
    [InlineData(100.00, 100.10)]
    [InlineData(1000.00, 1001.00)]
    [InlineData(2950.00, 2952.95)]
    [InlineData(2948.00, 2950.90)]
    [InlineData(1234.50, 1235.70)]
    [InlineData(1.30, 1.30)]
    public void The_buy_limit_is_the_trigger_close_plus_a_tenth_of_a_percent_rounded_down_to_the_tick(double triggerClose, double limit)
    {
        // The limit is the highest price at which the open is still "within the limit": an open on it fills, one tick above it is a miss.
        var stop = Math.Round(triggerClose * 0.8, 2);
        var onTheLimit = Hand.Buy("limit", triggerClose, stop).Rows(Row("10:00", limit, limit, limit, limit)).Walk(FillRules.Default);
        var oneTickAbove = Hand.Buy("limit+", triggerClose, stop).Rows(Row("10:00", limit + 0.05, limit + 0.05, limit + 0.05, limit + 0.05)).Walk(FillRules.Default);

        onTheLimit.Trade.ShouldNotBeNull().EntryFill.ShouldBe(EntryFill.AtOpen);
        oneTickAbove.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
    }

    [Theory]
    [InlineData(100.00, 99.90)]
    [InlineData(2950.00, 2947.05)]
    [InlineData(200.00, 199.80)]
    public void The_sell_limit_is_the_trigger_close_minus_a_tenth_of_a_percent_rounded_up_to_the_tick(double triggerClose, double limit)
    {
        var stop = Math.Round(triggerClose * 1.2, 2);
        var onTheLimit = Hand.Sell("limit", triggerClose, stop).Rows(Row("10:00", limit, limit, limit, limit)).Walk(FillRules.Default);
        var oneTickBelow = Hand.Sell("limit-", triggerClose, stop).Rows(Row("10:00", limit - 0.05, limit - 0.05, limit - 0.05, limit - 0.05)).Walk(FillRules.Default);

        onTheLimit.Trade.ShouldNotBeNull().EntryFill.ShouldBe(EntryFill.AtOpen);
        oneTickBelow.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
    }

    [Fact]
    public void Plain_double_arithmetic_gets_the_grid_wrong_which_is_why_the_walker_computes_in_decimal()
    {
        // The trap of T3: floor(100.00 x 1.001 / 0.05) x 0.05 is about 100.05, not 100.10; 1,000.95 and 2,952.90 for the second and third (limits 1,001.00 and 2,952.95).
        double Naive(double close) => Math.Floor(close * 1.001 / 0.05) * 0.05;

        Naive(100.00).ShouldBe(100.05, 0.001);
        Naive(1000.00).ShouldBe(1000.95, 0.001);
        Naive(2950.00).ShouldBe(2952.90, 0.001);
    }

    [Fact]
    public void A_limit_equal_to_the_trigger_close_when_a_tenth_of_a_percent_is_under_one_tick_still_fills_at_an_open_on_it()
    {
        // Tick 0.10 and close 20.00: 0.1 % is 0.02, under a tick, so the limit is the close rounded down (20.00): H25's entry.
        var onIt = Hand.Buy("tick10", 20.00, 19.00).WithTick(0.10m).Rows(Row("10:00", 20.00, 20.00, 20.00, 20.00)).Walk(FillRules.Default);
        var above = Hand.Buy("tick10+", 20.00, 19.00).WithTick(0.10m).Rows(Row("10:00", 20.10, 20.10, 20.10, 20.10)).Walk(FillRules.Default);

        onIt.Trade.ShouldNotBeNull().EntryFill.ShouldBe(EntryFill.AtOpen);
        above.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
    }

    [Fact]
    public void A_miss_is_final_a_later_minute_within_the_limit_does_not_fill_it()
    {
        // N1: 10:00 opens above the limit 100.10 and stays above; 10:01 would have been within it.
        var outcome = Hand.Buy("N1").Rows("10:00 100.40/100.60/100.20/100.50", "10:01 100.05/100.15/100.00/100.10").Walk(FillRules.Default);

        outcome.Trade.ShouldBeNull();
        outcome.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
        outcome.BarsExamined.ShouldBe(1);
        outcome.ToCounters().LimitNotReached.ShouldBe(1);
    }

    [Fact]
    public void A_fill_at_or_through_the_stop_is_no_trade()
    {
        // N2: stop 99.50 and the fill minute opens at 99.40.
        var outcome = Hand.Buy("N2", 100.00, 99.50).Rows("10:00 99.40/99.80/99.30/99.70").Walk(FillRules.Default);

        outcome.NoTrade.ShouldBe(NoTradeReason.EntryThroughStop);
        outcome.BarsExamined.ShouldBe(1);
    }

    [Fact]
    public void A_decision_at_the_last_minute_of_the_day_is_no_fill_bar()
    {
        // N3: the candidate's own minute is the last one of the date.
        var outcome = Hand.Buy("N3").EndsAfter("09:59").Walk(FillRules.Default);

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        outcome.BarsExamined.ShouldBe(0);
    }

    [Fact]
    public void A_minute_of_the_next_date_is_never_a_fill_minute()
    {
        var first = Day.Dense(last: "15:29").Start(100.00).Bars();
        var next = Day.Dense(new DateOnly(2026, 3, 3), last: "09:20").Start(100.00).Bars();
        Bar[] bars = [.. first, .. next];
        var candidate = Cand.Make(Side.Buy, first.Length - 1, "15:30", 99.00, "15:15", (100.00, 100.00, 100.00, 100.00));

        var outcome = TradeWalker.Walk(candidate, bars, Cand.Modelled).Value;

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Fact]
    public void A_decision_at_the_square_off_is_after_square_off()
    {
        // N4: the decision instant is 15:15 (the candidate's own minute is 15:14), so the fill minute 15:15 starts at Q.
        var outcome = Hand.Buy("N4").DecidesAt("15:15").Walk(FillRules.Default);

        outcome.NoTrade.ShouldBe(NoTradeReason.AfterSquareOff);
        outcome.BarsExamined.ShouldBe(0);
    }

    [Fact]
    public void Minutes_the_order_rested_through_still_fill_it_at_the_limit_when_the_minute_trades_back_to_it()
    {
        // N5: 10:00 to 10:02 absent; 10:03 opens at 100.20 and its low 100.10 touches the limit.
        var outcome = Hand.Buy("N5").Rows("10:03 100.20/100.40/100.10/100.30").Absent("10:00", "10:02").Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.EntryFill.ShouldBe(EntryFill.AtLimit);
        trade.EntryPrice.ShouldBe(100.10);
        trade.EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:03")));
        outcome.EntryAfterMissingMinutes.ShouldBeTrue();
    }

    [Theory]
    [InlineData("10:04", true)]
    [InlineData("10:05", false)]
    public void A_hole_of_exactly_five_minutes_before_the_fill_minute_fills_and_six_is_no_fill_bar(string lastAbsent, bool fills)
    {
        // The decision instant is 10:00; the fill minute may start at 10:05 (5 minutes after) but not at 10:06.
        var outcome = Hand.Buy("hole").Absent("10:00", lastAbsent).Walk(FillRules.Default);

        (outcome.Trade is not null).ShouldBe(fills);
        outcome.NoTrade.ShouldBe(fills ? null : NoTradeReason.NoFillBar);
    }

    [Fact]
    public void Empty_minutes_before_the_entry_are_passed_over_like_absent_ones_and_the_bound_counts_from_the_decision_instant()
    {
        // H24's first rows: 10:00 would be a miss but holds no trade; the fill minute is 10:01 and ZeroVolumeMinutes counts the passed minute.
        var fills = Hand.Buy("empty-before").Rows("10:00 100.60/100.60/100.60/100.60 v0", "10:01 100.00/100.20/99.90/100.10").Walk(FillRules.Default);
        var tooLate = Hand.Buy("empty-too-long").Rows("10:00 100.00/100.00/100.00/100.00 v0", "10:01 100.00/100.00/100.00/100.00 v0", "10:02 100.00/100.00/100.00/100.00 v0", "10:03 100.00/100.00/100.00/100.00 v0", "10:04 100.00/100.00/100.00/100.00 v0", "10:05 100.00/100.00/100.00/100.00 v0").Walk(FillRules.Default);

        fills.Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:01")));
        fills.ZeroVolumeMinutes.ShouldBe(1);
        fills.EntryAfterMissingMinutes.ShouldBeTrue();
        tooLate.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        tooLate.ZeroVolumeMinutes.ShouldBe(6);
    }

    [Fact]
    public void An_empty_minute_is_not_tested_for_the_stop_or_the_target_and_is_not_counted_in_the_excursions()
    {
        // 10:20 would be a stop (low 98.50) and 10:30 a target (high 103.00) but both are empty; the trade runs to the square-off at 15:15 and its MAE is only the fill minute's.
        var hand = Hand.Buy("empty-inside").Rows("10:00 100.00/100.30/99.80/100.20", "10:20 100.20/100.30/98.50/100.20 v0", "10:30 100.20/103.00/100.20/100.20 v0", "15:15 100.40/100.50/100.30/100.45");

        var outcome = hand.Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.SquareOff);
        trade.ExitPrice.ShouldBe(100.40);
        Math.Round(trade.MaeR, 4).ShouldBe(0.2);
        Math.Round(trade.MfeR, 4).ShouldBe(0.4);
        outcome.ZeroVolumeMinutes.ShouldBe(2);
    }

    [Fact]
    public void A_day_that_ends_with_empty_minutes_exits_at_the_close_of_the_last_minute_that_holds_a_trade()
    {
        var outcome = Hand.Buy("empty-tail").Rows("10:00 100.00/100.30/99.80/100.20", "14:00 100.40/100.50/100.30/100.45", "14:01 100.45/101.00/99.50/100.45 v0", "14:02 100.45/100.45/100.45/100.45 v0").EndsAfter("14:02").Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.EndOfData);
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("14:00")));
        trade.ExitPrice.ShouldBe(100.45);
        outcome.ZeroVolumeMinutes.ShouldBe(0, "the empty minutes after the last minute that holds a trade are not between the decision and the exit");
    }

    [Fact]
    public void A_walk_with_a_tick_from_the_fallback_marks_every_trade_and_a_master_tick_does_not()
    {
        var hand = HandCases.Get("H1").Hand;
        var bars = hand.Bars();
        var candidate = hand.Candidate(bars);

        var fallback = TradeWalker.Walk(candidate, bars, hand.Rules(FillRules.Default) with { Tick = TickSpec.FromMasterPaise(null).Value }).Value;
        var master = TradeWalker.Walk(candidate, bars, hand.Rules(FillRules.Default) with { Tick = TickSpec.FromMasterPaise(5m).Value }).Value;

        fallback.Trade.ShouldNotBeNull().TickFallback.ShouldBeTrue();
        master.Trade.ShouldNotBeNull().TickFallback.ShouldBeFalse();
        fallback.Trade.EntryFilledPrice.ShouldBe(master.Trade.EntryFilledPrice);
    }

    [Fact]
    public void A_walk_rules_value_that_bypassed_the_factories_is_refused_by_name()
    {
        var hand = HandCases.Get("H1").Hand;
        var bars = hand.Bars();
        var candidate = hand.Candidate(bars);

        var noFills = TradeWalker.Walk(candidate, bars, default);
        var noTick = TradeWalker.Walk(candidate, bars, default(WalkRules) with { Fills = FillRules.Default, SquareOffMinute = 915 });

        noFills.IsFailure.ShouldBeTrue();
        noFills.Error.Code.ShouldBe("simulation.fill_rules_invalid");
        noTick.IsFailure.ShouldBeTrue();
        noTick.Error.Code.ShouldBe("simulation.tick_size_invalid");
    }

    [Fact]
    public void A_price_that_is_not_a_finite_number_in_a_visited_minute_is_a_failure_and_not_an_exception()
    {
        var hand = Hand.Buy("nan").Rows("10:00 100.00/100.30/99.80/100.20");
        var bars = hand.Bars();
        var candidate = hand.Candidate(bars);
        var index = candidate.DecisionBarIndex + 1;
        bars[index] = bars[index] with { High = double.NaN };

        var result = TradeWalker.Walk(candidate, bars, hand.Rules(FillRules.Default));

        result.Error.Code.ShouldBe("simulation.bar_price_invalid");
    }

    [Fact]
    public void A_trigger_close_that_is_not_a_finite_number_is_rules_invalid()
    {
        var hand = HandCases.Get("H1").Hand;
        var bars = hand.Bars();
        var candidate = hand.Candidate(bars);

        var result = TradeWalker.Walk(candidate with { TriggerBar = candidate.TriggerBar with { Close = double.NaN } }, bars, hand.Rules(FillRules.Default));

        result.Error.Code.ShouldBe("simulation.rules_invalid");
    }
}
