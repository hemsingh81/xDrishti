using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// ISS-13, adversarial, the whole language chain (text, parser, binder, lint, compiler, frame) over hand-calculated sessions. The rule being tested: a per-session feature (vwap, today_*, prev_day_*, gap_pct, or_*) has
/// no history from an earlier session, so every read that reaches back across the open is unknown, and unknown is not true. Each word lists the truth after each bar (T, F or U).
/// <code>
/// Fixture TwoSessions (volume 10 on every bar; Monday's bars are flat, Tuesday's first bar is open 119 high 123 low 117 close 122):
/// bar:         1    2    3  |  4        5        6        7
/// close:       100  98   96 |  122      121      119      130
/// vwap:        100  99   98 |  120.667  120.833  120.222  122.667   (the running mean of the typical prices; Tuesday's first typical is (123 + 117 + 122) / 3)
/// today_open:  100  100 100 |  119      119      119      119
/// time (close of the bar):  09:16 09:17 09:18 | 09:16 09:17 09:18 09:19
/// </code>
/// </summary>
public sealed class SessionRestartConditionsAdversarialTests
{
    private static Bar[] TwoSessions() =>
    [
        Clock.Bar(Clock.Mon, Clock.Open, 100, 100, 100, 100),
        Clock.Close(Clock.Mon, Clock.Open + 1, 98),
        Clock.Close(Clock.Mon, Clock.Open + 2, 96),
        Clock.Bar(Clock.Tue, Clock.Open, 119, 123, 117, 122),
        Clock.Close(Clock.Tue, Clock.Open + 1, 121),
        Clock.Close(Clock.Tue, Clock.Open + 2, 119),
        Clock.Close(Clock.Tue, Clock.Open + 3, 130),
    ];

    /// <summary>
    /// Worked by hand, atomic builtins (a crossing or a run is unknown when any value it reads is):
    /// <list type="bullet">
    /// <item>crosses_above(close, vwap): bars 1 U (no bar before); 2: 98 &gt; 99 no; 3 no; 4: Tuesday's first, vwap[1] unknown: U; 5: 121 &gt; 120.833 but 122 &lt;= 120.667 no; 6 no; 7: 130 &gt; 122.667 and 119 &lt;= 120.222 yes.</item>
    /// <item>rising(vwap, 2): needs vwap[2]: U on bars 1, 2; 3: 98 &gt; 99 no; 4, 5: lag 2 unknown: U; 6: 120.222 &gt; 120.833 no; 7: 122.667 &gt; 120.222 but 120.222 &gt; 120.833 no.</item>
    /// <item>not crosses_below(close, vwap): crosses_below is U at 1 and 4, T at 2 (98 &lt; 99 and 100 &gt;= 100) and 6 (119 &lt; 120.222 and 121 &gt;= 120.833), F elsewhere; not turns T/F and keeps U.</item>
    /// <item>between(vwap[1], 99.5, 121): vwap[1] unknown on 1 and 4; 100 yes; 99 no; 120.667, 120.833, 120.222 yes.</item>
    /// <item>today_open[1] == 100: unknown on the first bar of each session; Monday's bars 2, 3 read 100; Tuesday reads 119.</item>
    /// <item>close[1] &gt; today_open: close is continuous: bar 4 reads 96 &gt; 119 no (the lag of the continuous feature is Monday's last close, which is fine); bar 5: 122 &gt; 119; 6: 121 &gt; 119; 7: 119 &gt; 119 no.</item>
    /// <item>level forms: close &gt; vwap and close[1] &lt;= vwap reads vwap at lag 0 only (bar 4: 122 &gt; 120.667 and 96 &lt;= 120.667: T). close &gt; vwap and close[1] &lt;= vwap[1] is U at bar 4 and F at bar 1 (100 &gt; 100 no, false and unknown is false).</item>
    /// <item>time guards: time is the close of the bar; "09:17" is false on bars 1 and 4 so those are F (and F elsewhere but 7); "09:16" is true on bars 1 and 4 so the unknown stays.</item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData("crosses_above(close, vwap)", "UFFUFFT")]
    [InlineData("rising(vwap, 1)", "UFFUTFT")]
    [InlineData("rising(vwap, 2)", "UUFUUFF")]
    [InlineData("crosses_above(close, today_open)", "UFFUFFT")]
    [InlineData("close > vwap and close[1] <= vwap[1]", "FFFUFFT")]
    [InlineData("close > vwap and close[1] <= vwap", "FFFTFFT")]
    [InlineData("close > today_open and close[1] <= today_open", "FFFTFFT")]
    [InlineData("rising(close, 1)", "UFFTFFT")]
    [InlineData("not crosses_below(close, vwap)", "UFTUTFT")]
    [InlineData("between(vwap[1], 99.5, 121)", "UTFUTTT")]
    [InlineData("today_open[1] == 100", "UTTUFFF")]
    [InlineData("close[1] > today_open", "UFFFTTF")]
    [InlineData("crosses_above(close, vwap) and time >= \"09:17\"", "FFFFFFT")]
    [InlineData("crosses_above(close, vwap) and time >= \"09:16\"", "UFFUFFT")]
    public void A_condition_over_two_sessions_has_the_truth_worked_out_by_hand(string condition, string word)
    {
        var rig = Rig.Of(Timeframe.M1, condition);

        rig.Run(TwoSessions()).ShouldBe(word);
    }

    private static readonly Timeframe[] IntradayTimeframes = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    [Theory]
    [InlineData("crosses_above(close, vwap)")]
    [InlineData("crosses_above(close, vwap) and time >= \"09:45\"")]
    [InlineData("rising(vwap, 3)")]
    [InlineData("close > vwap and close[1] <= vwap[1]")]
    [InlineData("crosses_above(close, today_open)")]
    [InlineData("crosses_above(close, prev_day.high)")]
    [InlineData("not crosses_below(close, vwap)")]
    [InlineData("between(vwap[1], 90, 110)")]
    public void The_headline_idioms_compile_with_no_problem_at_all_on_every_intraday_timeframe(string condition)
    {
        foreach (var timeframe in IntradayTimeframes)
        {
            var result = Compiling.Of(condition, timeframe);

            result.IsSuccess.ShouldBeTrue(condition + " on " + timeframe + ": " + Compiling.Describe(result));
            result.Problems.ShouldBeEmpty(condition + " on " + timeframe);
        }
    }

    [Fact]
    public void Unknown_is_not_true_so_a_decision_on_the_first_bar_of_a_session_is_false_and_the_truth_is_unknown()
    {
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, vwap)");
        var bars = TwoSessions();
        for (var i = 0; i < 3; i++)
        {
            rig.Step(bars[i]);
        }

        rig.Step(bars[3]);

        rig.Bound[0].EvaluateTruth(rig.Frame).ShouldBe(Truth.Unknown);
        rig.Bound[0].Evaluate(rig.Frame).ShouldBeFalse("unknown is not true");
    }

    /// <summary>
    /// Three sessions, flat bars (so highs and lows are the closes): Monday 100 98 96 (high 100), Tuesday 122 121 119 130 (high 130), Wednesday 140 128 135. prev_day.high is NaN on Monday, 100 on Tuesday, 130 on Wednesday.
    /// The gap-up first bars (122 &gt; 100, 140 &gt; 130) are the "through yesterday's level" the level form is for.
    /// </summary>
    private static Bar[] ThreeSessions() =>
    [
        .. Clock.Closes(Clock.Mon, 100, 98, 96),
        .. Clock.Closes(Clock.Tue, 122, 121, 119, 130),
        .. Clock.Closes(Clock.Wed, 140, 128, 135),
    ];

    [Theory]
    // crosses_above(close, prev_day.high): Monday has no previous day (U); Tuesday 1: lag 1 unknown (U); 2: 121 > 100 but 122 <= 100 no; 3, 4 no; Wednesday 1: U (the old reading was F: 130 <= 100 no);
    // 2: 128 > 130 no; 3: 135 > 130 and 128 <= 130 yes
    [InlineData("crosses_above(close, prev_day.high)", "UUUUFFFUFT")]
    // the level form reads prev_day.high at lag 0: Monday U (close > NaN and close[1] <= NaN); Tuesday 1: 122 > 100 and 96 <= 100 yes (gap through the level); 2: 122 <= 100 no; 3, 4 no; Wednesday 1: 140 > 130 and 130 <= 130 yes;
    // 2: 128 > 130 no; 3: 135 > 130 and 128 <= 130 yes
    [InlineData("close > prev_day.high and close[1] <= prev_day.high", "UUUTFFFTFT")]
    // prev_day.high[1] == prev_day.high: the lag reaches the previous session on the first bar of each: U there; equal afterwards
    [InlineData("prev_day.high[1] == prev_day.high", "UUUUTTTUTT")]
    public void The_previous_days_high_is_unknown_across_the_open_and_the_level_form_still_catches_the_gap(string condition, string word)
    {
        var rig = Rig.Of(Timeframe.M1, condition);

        rig.Run(ThreeSessions()).ShouldBe(word);
    }

    // ---- coarser lanes ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Sessions of whole 15-minute buckets: flat bars at one price per bucket (volume 10 a minute), so every 15m close equals the 15m vwap increment.</summary>
    private static (Bar[] Bars, int[] SessionStarts) FifteenMinuteSessions(double[][] prices)
    {
        var bars = new List<Bar>();
        var starts = new List<int>();
        var dates = new[] { Clock.Mon, Clock.Tue, Clock.Wed };
        for (var s = 0; s < prices.Length; s++)
        {
            starts.Add(bars.Count);
            for (var b = 0; b < prices[s].Length; b++)
            {
                for (var i = 0; i < 15; i++)
                {
                    bars.Add(Clock.Close(dates[s], Clock.Open + b * 15 + i, prices[s][b]));
                }
            }
        }

        return ([.. bars], [.. starts]);
    }

    [Fact]
    public void On_a_fifteen_minute_strategy_a_crossing_of_the_vwap_is_unknown_until_the_second_bucket_of_every_session()
    {
        // Monday buckets 100 98 96 -> 15m vwap 100, 99, 98. Tuesday buckets 122 121 119 130 -> vwap 122, 121.5, 120.667, 123.
        // Sampled after the last minute of each bucket: Monday U (lag 1 unknown) F F; Tuesday U (the second bucket is needed) F F T (130 > 123 and 119 <= 120.667).
        // And at every minute of Tuesday before its first bucket has closed (45 .. 58): the lane is pending, vwap is not ready: U — the old frame read yesterday's vwap there and answered F.
        var (bars, starts) = FifteenMinuteSessions([[100, 98, 96], [122, 121, 119, 130]]);
        var rig = Rig.Of(Timeframe.M15, "crosses_above(close, vwap)");
        var truths = bars.Select(bar => rig.Step(bar)[0]).ToArray();

        starts[1].ShouldBe(45);
        for (var i = 0; i < 14; i++)
        {
            truths[i].ShouldBe(Truth.Unknown, $"Monday minute {i}: nothing has closed");
        }

        new[] { truths[14], truths[29], truths[44] }.ShouldBe([Truth.Unknown, Truth.False, Truth.False]);
        for (var i = 45; i < 59; i++)
        {
            truths[i].ShouldBe(Truth.Unknown, $"Tuesday minute {i - 45}: the 15m lane is pending");
        }

        new[] { truths[59], truths[74], truths[89], truths[104] }.ShouldBe([Truth.Unknown, Truth.False, Truth.False, Truth.True]);
    }

    [Fact]
    public void A_previous_days_close_of_a_coarser_lane_is_unknown_until_that_lane_has_closed_a_bar_of_the_date_and_then_the_right_day()
    {
        // 1h buckets on a 1m strategy, three sessions of three hours: Monday 100 110 130, Tuesday 120 90 140, Wednesday 150 150 150. tf("1h").prev_day_close is NaN on Monday, 130 on Tuesday, 140 on Wednesday, but each
        // only once the 1h lane has closed its first bar of the date (the 60th minute): until then it is pending. The old frame showed the earlier day's value and, on Wednesday, Monday's 130.
        var prices = new[] { new double[] { 100, 110, 130 }, [120, 90, 140], [150, 150, 150] };
        var dates = new[] { Clock.Mon, Clock.Tue, Clock.Wed };
        var rig = Rig.Of(Timeframe.M1, ["tf(\"1h\").prev_day_close == 130", "tf(\"1h\").prev_day_close == 140", "tf(\"1h\").today_open == 150"]);
        var all = new List<Truth[]>();
        for (var s = 0; s < 3; s++)
        {
            for (var minute = 0; minute < 180; minute++)
            {
                all.Add(rig.Step(Clock.Close(dates[s], Clock.Open + minute, prices[s][minute / 60])));
            }
        }

        // Monday: no previous day anywhere; Tuesday: pending for 59 minutes
        for (var i = 0; i < 239; i++)
        {
            all[i][0].ShouldBe(Truth.Unknown, $"minute {i}: prev_day_close == 130 before Tuesday's first hour has closed");
        }

        for (var i = 239; i < 360; i++)
        {
            all[i][0].ShouldBe(Truth.True, $"minute {i}: Tuesday's first hour has closed, the previous day closed at 130");
            all[i][1].ShouldBe(Truth.False, $"minute {i}: not 140 on Tuesday");
        }

        for (var i = 360; i < 419; i++)
        {
            all[i][0].ShouldBe(Truth.Unknown, $"Wednesday minute {i - 360}: pending, and certainly not yesterday's or the day before's 130");
            all[i][1].ShouldBe(Truth.Unknown, $"Wednesday minute {i - 360}: pending");
            all[i][2].ShouldBe(Truth.Unknown, $"Wednesday minute {i - 360}: today_open (1h) is pending");
        }

        for (var i = 419; i < 540; i++)
        {
            all[i][0].ShouldBe(Truth.False, $"Wednesday minute {i - 360}: the previous day closed at 140");
            all[i][1].ShouldBe(Truth.True, $"Wednesday minute {i - 360}");
            all[i][2].ShouldBe(Truth.True, $"Wednesday minute {i - 360}: today_open (1h)");
        }
    }

    // ---- 1D ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Four days told by a 09:15 bar and a 15:29 bar: open / close 100 / 105, 108 / 118, 115 / 112, 111 / 124. Daily gaps: +2.857% (108 over 105), -2.542% (115 under 118), -0.893% (111 under 112).</summary>
    private static Bar[] FourDays()
    {
        var days = new[] { Clock.Mon, Clock.Tue, Clock.Wed, Clock.Thu };
        var days4 = new (double Open, double Close)[] { (100, 105), (108, 118), (115, 112), (111, 124) };
        return [.. days.SelectMany((d, i) => new[] { Clock.Bar(d, Clock.Open, days4[i].Open, days4[i].Open, days4[i].Open, days4[i].Open, 10), Clock.Bar(d, Clock.At(15, 29), days4[i].Close, days4[i].Close, days4[i].Close, days4[i].Close, 10) })];
    }

    [Theory]
    // evaluated at the 15:29 bar of each day, when the daily bar has just closed:
    // gap_pct[1]: the gap of the day before: unknown on day 1 and 2 (day 2's lag 1 is day 1, which has no gap), +2.857 > 0 on day 3, -2.542 on day 4
    [InlineData("gap_pct[1] > 0", "UUTF")]
    // prev_day_close[1] == 105: day 2's lag 1 is day 1's prev_day_close (none: unknown); day 3's is 105 (the previous close of day 2); day 4's is 118
    [InlineData("prev_day_close[1] == 105", "UUTF")]
    // today_open[1] == 100: day 1 unknown; day 2's lag 1 is day 1's open
    [InlineData("today_open[1] == 100", "UTFF")]
    public void On_a_daily_strategy_the_session_features_are_read_by_position_and_nothing_goes_pending(string condition, string word)
    {
        var rig = Rig.Of(Timeframe.D1, condition);
        var bars = FourDays();
        var atClose = new List<Truth>();
        for (var i = 0; i < bars.Length; i++)
        {
            var truth = rig.Step(bars[i])[0];
            if (i % 2 == 1)
            {
                atClose.Add(truth);
            }
        }

        Rig.Word(atClose).ShouldBe(word);
    }

    [Fact]
    public void The_same_lag_on_a_one_minute_strategy_is_unknown_on_the_first_bar_of_the_day_where_the_daily_strategy_has_a_value()
    {
        // 1m: prev_day_close[1] == 105 at Tuesday's first bar (the lag reaches into Monday's session): unknown. Daily bars are unrelated here. 1m frame, same four days.
        var rig = Rig.Of(Timeframe.M1, "prev_day_close[1] == prev_day_close");
        var truths = FourDays().Select(bar => rig.Step(bar)[0]).ToArray();

        // bars: Mon 09:15, Mon 15:29, Tue 09:15, Tue 15:29, ... prev_day_close is NaN on Monday; Tue 09:15 first bar -> U; Tue 15:29 second bar -> prev_day_close[1] is the 09:15 value = 105 = prev_day_close: T
        Rig.Word(truths).ShouldBe("UUUTUTUT");
    }
}
