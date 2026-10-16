using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 9: conditions evaluated over frames of at most 12 bars, with the result at every bar worked out by hand and written as a word (<c>T</c> true, <c>F</c> false, <c>U</c> unknown). The bars are invented; the
/// tables are calculated from the definitions in the spec, not from the library.
/// </summary>
public sealed class HandExamplesTests
{
    private static readonly DateOnly Mon = Clock.Mon;

    /// <summary>Twelve bars with the open at 10 all the time and the closes 9, 10, 11, 12, 10, 9, 10, 11, 10, 11, 8, 12: a touch, a jump over, an equality at the previous and at the current bar.</summary>
    private static Bar[] CrossingBars() =>
        [.. new double[] { 9, 10, 11, 12, 10, 9, 10, 11, 10, 11, 8, 12 }.Select((close, i) => Clock.OpenClose(Mon, Clock.Open + i, 10, close))];

    // ---- crosses ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Crosses_above_needs_a_close_above_the_open_now_and_not_above_it_at_the_previous_bar_and_is_unknown_on_the_first_bar()
    {
        // bar:   1  2  3  4  5  6  7  8  9  10 11 12
        // close: 9  10 11 12 10 9  10 11 10 11 8  12
        // 1: no previous bar. 2: 10 > 10 no. 3: 11 > 10 and previous 10 <= 10 (equal counts as not above) yes. 4: previous 11 <= 10 no.
        // 5, 7, 9: close equals the open now, 10 > 10 no. 8 and 10: previous close 10 equals the open, yes. 11: below. 12: 12 > 10 and previous 8 <= 10, yes (a jump over).
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, open)");

        rig.Run(CrossingBars()).ShouldBe("UFTFFFFTFTFT");
    }

    [Fact]
    public void Crosses_below_is_the_mirror_with_equality_at_the_previous_bar_counting_as_not_below_yet()
    {
        // 6: 9 < 10 and previous 10 >= 10 (equal counts as not below yet) yes. 5: close equals the open now (10 < 10 no) although the previous bar was above. 11: 8 < 10 and previous 11 >= 10, yes.
        var rig = Rig.Of(Timeframe.M1, "crosses_below(close, open)");

        rig.Run(CrossingBars()).ShouldBe("UFFFFTFFFFTF");
    }

    [Fact]
    public void A_crossing_is_unknown_on_the_first_bar_after_the_warm_up_and_has_a_value_from_the_next_one()
    {
        // sma(3) is ready from bar 3, so crosses_above(close, sma(3)) needs sma(3)[1]: ready from bar 4. Closes 10 10 10 12: sma(3) is 10 at bar 3 and 32/3 = 10.67 at bar 4.
        // Bar 3 is unknown (the average of bar 2 does not exist); bar 4: 12 > 10.67, and the previous close 10 <= the previous average 10: true.
        var rising = Rig.Of(Timeframe.M1, "crosses_above(close, sma(3))");
        rising.Run(Clock.Closes(Mon, 10, 10, 10, 12)).ShouldBe("UUUT");

        // Closes 12 11 10 9: sma(3) is 11 at bar 3 and 10 at bar 4. Bar 4: 9 > 10 no: false (not unknown) as soon as the history is long enough.
        var falling = Rig.Of(Timeframe.M1, "crosses_above(close, sma(3))");
        falling.Run(Clock.Closes(Mon, 12, 11, 10, 9)).ShouldBe("UUUF");
    }

    [Fact]
    public void Crosses_of_expressions_read_every_feature_in_them_one_bar_back()
    {
        // close - open crosses above 1: bars (open, close): (10,10) (10,11) (10,12) (10,13) -> close - open = 0, 1, 2, 3. At bar 3: 2 > 1 and previous 1 <= 1: true. At bar 4: previous 2 <= 1 no: false.
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close - open, 1)");
        var bars = new double[] { 10, 11, 12, 13 }.Select((c, i) => Clock.OpenClose(Mon, Clock.Open + i, 10, c));

        rig.Run(bars).ShouldBe("UFTF");
    }

    // ---- rising and falling ----------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Rising_needs_n_strict_steps_up_and_n_plus_one_values()
    {
        // closes: 1 2 3 5 5 6 7 8 7 6 5 4. rising(close, 3) is close[0] > close[1] > close[2] > close[3].
        // 1-3: fewer than four values. 4: 5>3>2>1. 5: 5>5 no. 6: 6>5>5 no (equal steps are not rising). 7: 7>6>5>5 no. 8: 8>7>6>5 yes. 9-12: falling.
        var rig = Rig.Of(Timeframe.M1, "rising(close, 3)");

        rig.Run(Clock.Closes(Mon, 1, 2, 3, 5, 5, 6, 7, 8, 7, 6, 5, 4)).ShouldBe("UUUTFFFTFFFF");
    }

    [Fact]
    public void Falling_is_the_mirror()
    {
        // 9: 7<8 yes, 8<7 no. 10: 6<7<8, then 7<... the third step 8<7 no. 11: closes 5 6 7 8: 5<6<7<8 yes. 12: 4<5<6<7 yes.
        var rig = Rig.Of(Timeframe.M1, "falling(close, 3)");

        rig.Run(Clock.Closes(Mon, 1, 2, 3, 5, 5, 6, 7, 8, 7, 6, 5, 4)).ShouldBe("UUUFFFFFFFTT");
    }

    [Fact]
    public void Rising_of_one_step_compares_with_the_previous_bar_only()
    {
        var rig = Rig.Of(Timeframe.M1, "rising(close, 1)");

        rig.Run(Clock.Closes(Mon, 5, 6, 6, 7, 4)).ShouldBe("UTFTF");
    }

    [Fact]
    public void Rising_of_an_indicator_is_unknown_until_n_plus_one_values_of_it_exist()
    {
        // sma(2) is ready from bar 2; rising(sma(2), 2) needs three values: bars 2, 3, 4 -> first value at bar 4. Closes 1 2 3 5: sma(2) = 1.5 2.5 4 -> 4 > 2.5 > 1.5 yes.
        var rig = Rig.Of(Timeframe.M1, "rising(sma(2), 2)");

        rig.Run(Clock.Closes(Mon, 1, 2, 3, 5, 1)).ShouldBe("UUUTF");
    }

    // ---- between ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Between_includes_both_ends()
    {
        var rig = Rig.Of(Timeframe.M1, "between(close, 10, 20)");

        rig.Run(Clock.Closes(Mon, 9.99, 10, 15, 20, 20.01)).ShouldBe("FTTTF");
    }

    [Fact]
    public void Between_is_unknown_when_any_of_its_three_values_is_not_ready()
    {
        // The value is not ready for two bars (sma(3) is 12, 12, then (12+12+60)/3 = 28); the high end is not ready for the first bar only (sma(2) is 12, then 36 at bar 3).
        Rig.Of(Timeframe.M1, "between(sma(3), 10, 20)").Run(Clock.Closes(Mon, 12, 12, 12, 60)).ShouldBe("UUTF");
        Rig.Of(Timeframe.M1, "between(close, 5, sma(2))").Run(Clock.Closes(Mon, 12, 12, 60)).ShouldBe("UTF");
        Rig.Of(Timeframe.M1, "between(close, sma(2), 100)").Run(Clock.Closes(Mon, 12, 12, 60)).ShouldBe("UTT");
    }

    // ---- subscripts ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_subscript_reads_the_value_k_closed_bars_ago()
    {
        // close[1] > close: the previous close is above the current one. Closes 5 6 6 4: bar 1 has no previous bar; 5 > 6 no; 6 > 6 no; 6 > 4 yes.
        Rig.Of(Timeframe.M1, "close[1] > close").Run(Clock.Closes(Mon, 5, 6, 6, 4)).ShouldBe("UFFT");

        // close[2] == 7 holds at the bar two after the 7.
        Rig.Of(Timeframe.M1, "close[2] == 7").Run(Clock.Closes(Mon, 7, 1, 1, 7, 1, 1)).ShouldBe("UUTFFT");
    }

    [Fact]
    public void A_higher_timeframe_subscript_counts_closed_bars_of_that_timeframe_and_ignores_the_forming_one()
    {
        // 1h bars start at 09:15. Bars (start, close): 09:15 100 | 09:40 150 | 10:14 101 (closes the first hour: close 101) | 10:30 555 | 11:14 102 (closes the second hour) | 11:30 777 | 12:14 103 (closes the third).
        var bars = new (int Minute, double Close)[] { (Clock.At(9, 15), 100), (Clock.At(9, 40), 150), (Clock.At(10, 14), 101), (Clock.At(10, 30), 555), (Clock.At(11, 14), 102), (Clock.At(11, 30), 777), (Clock.At(12, 14), 103) }
            .Select(b => Clock.Close(Mon, b.Minute, b.Close)).ToList();

        // tf("1h").close[1] is the close of the hour before the last closed one: unknown until two hours have closed (steps 1-4), 101 from the step that closes the second hour (5, 6), 102 after the third (7).
        Rig.Of(Timeframe.H1, "tf(\"1h\").close[1] == 101").Run(bars).ShouldBe("UUUUTTF");
        Rig.Of(Timeframe.H1, "tf(\"1h\").close[1] == 102").Run(bars).ShouldBe("UUUUFFT");

        // tf("1h").close is 101 from the step that closes the first hour until the step that closes the second one: the forming hour (150 at step 2, 555 at step 4) is never read.
        Rig.Of(Timeframe.H1, "tf(\"1h\").close == 101").Run(bars).ShouldBe("UUTTFFF");

        // On a 15m strategy the same feature is read from a frame that also has a 15m lane: the 1h value does not move at the 15m closes inside the hour.
        Rig.Of(Timeframe.M15, "tf(\"1h\").close[1] < tf(\"1h\").close", extra: [Specs.Close(Timeframe.M15)]).Run(bars).ShouldBe("UUUUTTT");
    }

    // ---- calendar and context --------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Day_of_week_is_the_iso_weekday_of_the_india_date_and_the_names_are_numbers()
    {
        // 2 March 2026 is a Monday, 3 March a Tuesday, 4 March a Wednesday.
        var bars = new[]
        {
            Clock.Close(Clock.Mon, Clock.Open, 100), Clock.Close(Clock.Mon, Clock.Open + 1, 100),
            Clock.Close(Clock.Tue, Clock.Open, 100), Clock.Close(Clock.Tue, Clock.Open + 1, 100),
            Clock.Close(Clock.Wed, Clock.Open, 100),
        };

        Rig.Of(Timeframe.M1, "day_of_week == TUE").Run(bars).ShouldBe("FFTTF");
        Rig.Of(Timeframe.M1, "day_of_week != TUE").Run(bars).ShouldBe("TTFFT");
        Rig.Of(Timeframe.M1, "day_of_week >= TUE and day_of_week < THU").Run(bars).ShouldBe("FFTTT");
        Rig.Of(Timeframe.M1, "day_of_week == 2").Run(bars).ShouldBe("FFTTF");
    }

    [Fact]
    public void The_weekday_of_the_previous_date_is_the_lag_one_value_and_does_not_exist_on_the_first_date()
    {
        var bars = new[] { Clock.Close(Clock.Mon, Clock.Open, 100), Clock.Close(Clock.Mon, Clock.Open + 1, 100), Clock.Close(Clock.Tue, Clock.Open, 100), Clock.Close(Clock.Tue, Clock.Open + 1, 100) };

        Rig.Of(Timeframe.M1, "day_of_week[1] == MON").Run(bars).ShouldBe("UUTT");
    }

    /// <summary>A context of two days: the VIX closed at 50 on Monday and at 30 on Tuesday. A frame reads the day before its date, so <c>vix</c> is 50 all day on Tuesday and 30 all day on Wednesday.</summary>
    private static MarketContext TwoDayVixContext() =>
        MarketContext.Create([new ContextDay(Clock.Mon, 100, 90, 95, 50), new ContextDay(Clock.Tue, 100, 90, 95, 30)], Contexts.Small).Value;

    /// <summary>Two bars on Tuesday (close 25) and three on Wednesday (close 40, above the VIX of 30 from the first minute of the day).</summary>
    private static Bar[] TuesdayAndWednesdayBars() =>
        [Clock.Close(Clock.Tue, Clock.Open, 25), Clock.Close(Clock.Tue, Clock.Open + 1, 25), Clock.Close(Clock.Wed, Clock.Open, 40), Clock.Close(Clock.Wed, Clock.Open + 1, 40), Clock.Close(Clock.Wed, Clock.Open + 2, 40)];

    [Fact]
    public void The_lag_of_a_feature_of_the_date_is_the_previous_date_and_not_the_previous_minute()
    {
        // vix is 50 on Tuesday (Monday's close) and 30 on Wednesday (Tuesday's close). vix[1] is the value of the previous date: not ready on the first date, 50 all day on the second.
        Rig.Of(Timeframe.M1, "vix[1] == 50", TwoDayVixContext()).Run(TuesdayAndWednesdayBars()).ShouldBe("UUTTT");
        Rig.Of(Timeframe.M1, "vix == 30", TwoDayVixContext()).Run(TuesdayAndWednesdayBars()).ShouldBe("FFTTT");
    }

    [Fact]
    public void A_crossing_of_a_bar_feature_with_the_context_is_refused_because_its_previous_value_would_be_the_previous_date_and_the_explicit_form_crosses_once()
    {
        // close was 25 on Tuesday and is 40 on Wednesday, vix is 30 on Wednesday: the close crosses above the vix once, at 09:15 on Wednesday, and then stays above it.
        // The explicit form says so: 09:15 Wed: 40 > 30 and the previous close 25 <= 30 yes; 09:16 and 09:17: the previous close 40 <= 30 no. On Tuesday the close is below the vix of 50.
        Rig.Of(Timeframe.M1, "close > vix and close[1] <= vix", TwoDayVixContext()).Run(TuesdayAndWednesdayBars()).ShouldBe("FFTFF");

        // crosses_above(close, vix) would have meant close > vix and close[1] <= vix[1] and vix[1] on Wednesday is Tuesday's 50 all day: 40 <= 50 at 09:16 and 09:17 as well, so the "crossing" would fire
        // at every minute of the date although the close never crossed anything again. This is the wrong answer the lint exists to refuse.
        Rig.Of(Timeframe.M1, "close > vix and close[1] <= vix[1]", TwoDayVixContext()).Run(TuesdayAndWednesdayBars()).ShouldBe("FFTTT");
        var refused = Compiling.Of("crosses_above(close, vix)", Timeframe.M1);

        refused.IsSuccess.ShouldBeFalse();
        var error = refused.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(LintCodes.MixedTimeframesInLag);
        error.Suggestion.ShouldBe("close > vix and close[1] <= vix");
    }

    [Fact]
    public void A_crossing_of_the_context_with_a_number_is_a_crossing_of_the_date_and_holds_for_the_whole_date()
    {
        // vix falls from 50 (Tuesday) to 30 (Wednesday). crosses_below(vix, 40) is vix < 40 and vix[1] >= 40 with the previous value of the previous date, so it holds on every minute of Wednesday: it is the
        // date that crossed, and the value of a date does not change within it. On Tuesday there is no previous date: unknown.
        Rig.Of(Timeframe.M1, "crosses_below(vix, 40)", TwoDayVixContext()).Run(TuesdayAndWednesdayBars()).ShouldBe("UUTTT");
    }

    /// <summary>
    /// Ten days of an index, close 100 102 104 103 101 102 105 105 99 98 (high and low one point away), with options that make every regime worked out by hand: the average is of 2 days, the slope over 1 day, the ADX
    /// of 1 day with a minimum of 0 (any ADX passes once it exists), the ATR of 1 day and a percentile window of 1 value, whose rank is always the middle (50), so the volatility is always normal. The trend of a day is
    /// then up when the close is above the previous close and above the close two days earlier, down for the mirror image, else range: day 3 (104) up, day 4 (103) range, day 5 (101) down, day 6 (102) range, day 7 (105) up,
    /// day 8 (105) range, day 9 (99) down, day 10 (98) down. The regime is <c>(1 - trend) * 3 + volatility + 1</c> with volatility 1: up 2 (UP_NORMAL_VOL), range 5 (RANGE_NORMAL_VOL), down 8 (DOWN_NORMAL_VOL).
    /// Days 1 and 2 have no trend yet.
    /// </summary>
    private static MarketContext TenDayRegimeContext()
    {
        double[] closes = [100, 102, 104, 103, 101, 102, 105, 105, 99, 98];
        var options = new ContextOptions(TrendSmaPeriod: 2, TrendSlopeDays: 1, AdxPeriod: 1, AdxMin: 0, AtrPeriod: 1, PercentileWindow: 1);
        var days = closes.Select((close, i) => new ContextDay(WeekdayOf(i), close + 1, close - 1, close)).ToList();

        return MarketContext.Create(days, options).Value;
    }

    /// <summary>The weekdays from Monday 2 March 2026: day 0 is that Monday and day 5 the Monday after the Friday 6 March.</summary>
    private static DateOnly WeekdayOf(int index) => Mon.AddDays((index / 5 * 7) + (index % 5));

    [Fact]
    public void The_regime_is_compared_with_its_name_or_its_code_and_is_the_value_of_the_day_before()
    {
        // One bar on each of eleven weekdays: the first is the date of the first context day, the eleventh the date after the last. A frame at a date reads the day before it:
        // date:    1  2  3  4  5  6  7  8  9  10 11
        // reads:   -  d1 d2 d3 d4 d5 d6 d7 d8 d9 d10
        // regime:  U  U  U  2  5  8  5  2  5  8  8      (no day before; days 1 and 2 have no trend yet; then the codes worked out above)
        var bars = Enumerable.Range(0, 11).Select(i => Clock.Close(WeekdayOf(i), Clock.Open, 100)).ToList();
        var context = TenDayRegimeContext();

        Rig.Of(Timeframe.M1, "regime() == \"UP_NORMAL_VOL\"", context).Run(bars).ShouldBe("UUUTFFFTFFF");
        Rig.Of(Timeframe.M1, "regime() == 2", context).Run(bars).ShouldBe("UUUTFFFTFFF");
        Rig.Of(Timeframe.M1, "\"UP_NORMAL_VOL\" == regime()", context).Run(bars).ShouldBe("UUUTFFFTFFF");
        Rig.Of(Timeframe.M1, "regime() == \"RANGE_NORMAL_VOL\"", context).Run(bars).ShouldBe("UUUFTFTFTFF");
        Rig.Of(Timeframe.M1, "regime() == \"DOWN_NORMAL_VOL\"", context).Run(bars).ShouldBe("UUUFFTFFFTT");
        Rig.Of(Timeframe.M1, "regime() != \"UP_NORMAL_VOL\"", context).Run(bars).ShouldBe("UUUFTTTFTTT");
        Rig.Of(Timeframe.M1, "regime() != 8", context).Run(bars).ShouldBe("UUUTTFTTTFF");
        Rig.Of(Timeframe.M1, "regime() == \"UP_LOW_VOL\"", context).Run(bars).ShouldBe("UUUFFFFFFFF");
    }

    // ---- time ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Bars that start at 09:15, 09:29, 09:30, 09:43, 09:44, 09:45 and 09:59: 15-minute buckets close at the bars that start at 09:29, 09:44 and 09:59.</summary>
    private static Bar[] QuarterBars() =>
        [.. new[] { Clock.At(9, 15), Clock.At(9, 29), Clock.At(9, 30), Clock.At(9, 43), Clock.At(9, 44), Clock.At(9, 45), Clock.At(9, 59) }.Select(m => Clock.Close(Mon, m, 100))];

    [Fact]
    public void Time_is_the_minute_at_which_the_evaluated_bar_closed_so_a_quarter_hour_bar_is_first_at_0945_when_the_minute_0944_has_closed_it()
    {
        // The bar that starts at 09:44 closes at 09:45: time = 09:45. Close times of the seven steps: 09:16 09:30 09:31 09:44 09:45 09:46 10:00.
        var extra = new[] { Specs.Close(Timeframe.M15) };

        Rig.Of(Timeframe.M15, "time >= \"09:45\"", extra: extra).Run(QuarterBars()).ShouldBe("FFFFTTT");
        Rig.Of(Timeframe.M15, "time == \"09:45\"", extra: extra).Run(QuarterBars()).ShouldBe("FFFFTFF");
        Rig.Of(Timeframe.M15, "time > \"09:45\"", extra: extra).Run(QuarterBars()).ShouldBe("FFFFFTT");
        Rig.Of(Timeframe.M15, "time < \"09:45\"", extra: extra).Run(QuarterBars()).ShouldBe("TTTTFFF");
        Rig.Of(Timeframe.M15, "time <= \"09:45\"", extra: extra).Run(QuarterBars()).ShouldBe("TTTTTFF");
        Rig.Of(Timeframe.M15, "time != \"09:45\"", extra: extra).Run(QuarterBars()).ShouldBe("TTTTFTT");
        Rig.Of(Timeframe.M15, "\"09:45\" <= time", extra: extra).Run(QuarterBars()).ShouldBe("FFFFTTT");
    }

    [Fact]
    public void On_a_15m_strategy_the_first_closed_bar_where_time_is_at_least_0945_is_the_one_that_closes_at_0945()
    {
        var rig = Rig.Of(Timeframe.M15, "time >= \"09:45\"", extra: [Specs.Close(Timeframe.M15)]);
        var firstTrue = new List<int>();
        var closedAt = new List<int>();

        foreach (var bar in QuarterBars())
        {
            rig.Frame.Step(bar);
            if (rig.Frame.BarClosed(Timeframe.M15))
            {
                closedAt.Add((int)((bar.EpochSeconds + 19_800) % 86_400 / 60));
                if (rig.Bound[0].Evaluate(rig.Frame))
                {
                    firstTrue.Add(closedAt[^1]);
                }
            }
        }

        closedAt.ShouldBe([Clock.At(9, 29), Clock.At(9, 44), Clock.At(9, 59)], "the 15-minute bars close with the minutes that start at 09:29, 09:44 and 09:59");
        firstTrue.ShouldBe([Clock.At(9, 44), Clock.At(9, 59)], "the bar that closes at 09:45 is the first with time >= 09:45; the bar before it closed at 09:30");
    }

    [Fact]
    public void Time_is_a_whole_minute_comparison_and_the_last_minute_of_the_session_closes_at_1530()
    {
        var rig = Rig.Of(Timeframe.M1, ["time == \"15:30\"", "time >= \"15:30\"", "time < \"09:16\""], extra: [Specs.Close(Timeframe.M1)]);

        rig.Step(Clock.Close(Mon, Clock.Open, 100)).ShouldBe([Truth.False, Truth.False, Truth.False], "09:15 closes at 09:16: not less than 09:16");
        rig.Step(Clock.Close(Mon, Clock.At(15, 28), 100)).ShouldBe([Truth.False, Truth.False, Truth.False]);
        rig.Step(Clock.Close(Mon, Clock.At(15, 29), 100)).ShouldBe([Truth.True, Truth.True, Truth.False]);
    }

    [Fact]
    public void Time_is_unknown_on_a_frame_that_has_accepted_no_bar_and_known_afterwards()
    {
        var rig = Rig.Of(Timeframe.M1, "time >= \"09:45\"", extra: [Specs.Close(Timeframe.M1)]);

        rig.Read().ShouldBe([Truth.Unknown]);
        rig.Bound[0].Evaluate(rig.Frame).ShouldBeFalse();
        rig.Step(Clock.Close(Mon, Clock.Open, 100)).ShouldBe([Truth.False]);

        rig.Frame.Reset();

        rig.Read().ShouldBe([Truth.Unknown], "Reset returns the frame to before the first bar");
    }
}
