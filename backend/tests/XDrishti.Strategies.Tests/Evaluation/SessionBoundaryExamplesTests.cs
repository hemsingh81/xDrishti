using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// A per-session feature keeps the history of its own session only (ADR 0035; before it, ADR 0034 Amendment 19 had a lagged read cross the session open): on the first bar of a session <c>vwap[1]</c>, <c>today_open[1]</c> and
/// <c>prev_day.high[1]</c> are unknown, not the last values of the session before, so the headline example <c>crosses_above(close, vwap)</c> cannot fire on the first bar of a session. Two sessions of at most four 1-minute
/// bars with the results worked out by hand pin what that does to the crossings, to the equivalent spellings and to the <c>time</c> check that is no longer needed; a continuous feature still runs on across sessions. The tables are
/// calculated from the definitions, not from the library.
/// </summary>
public sealed class SessionBoundaryExamplesTests
{
    /// <summary>
    /// Monday: three bars that slide under their own vwap (closes 100, 98, 96, volume 10 each, so vwap = the running mean of the closes: 100, 99, 98). Tuesday opens with a gap up: the first bar is
    /// (open 119, high 123, low 117, close 122), typical price (123 + 117 + 122) / 3 = 120.667; then closes 121, 119 and 130.
    /// <code>
    /// bar:     1    2    3  |  4        5        6        7
    /// close:   100  98   96 |  122      121      119      130
    /// vwap:    100  99   98 |  120.667  120.833  120.222  122.667   (Tuesday's mean of the typical prices: (120.667 + 121) / 2, (120.667 + 121 + 119) / 3, (… + 130) / 4)
    /// open of the day: 100  |  119
    /// time at the close of the bar: 09:16 09:17 09:18 | 09:16 09:17 09:18 09:19
    /// </code>
    /// A letter per bar: T true, F false, U unknown. The values one bar back that exist inside the session are the ones of the table; the first bar of a session has no bar before it in its own session.
    /// </summary>
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

    [Fact]
    public void Crosses_above_the_vwap_cannot_fire_on_the_first_bar_of_a_session_the_cross_of_two_sessions_is_unknown()
    {
        // bar 1: no bar before it, unknown. 2: 98 > 99 no. 3: 96 > 98 no.
        // bar 4: 122 > 120.667, but the bar before it in its own session does not exist: vwap[1] is unknown, so the crossing is unknown (it was a cross of Monday's last bar with Tuesday's first).
        // bar 5: 121 > 120.833, but the previous close 122 is above the previous vwap 120.667: no. 6: 119 > 120.222 no.
        // bar 7: 130 > 122.667 and the previous close 119 <= 120.222: true, a cross inside Tuesday.
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, vwap)");

        rig.Run(TwoSessions()).ShouldBe("UFFUFFT");
    }

    [Fact]
    public void Rising_vwap_is_unknown_on_the_first_bar_of_the_second_session_the_vwap_of_a_new_session_is_not_compared_with_the_old_one()
    {
        // vwap: 100, 99, 98 | 120.667, 120.833, 120.222, 122.667.
        // bar 1 unknown. 2: 99 > 100 no. 3: 98 > 99 no. 4: vwap[1] does not exist in Tuesday: unknown. 5: 120.833 > 120.667 yes. 6: 120.222 > 120.833 no. 7: 122.667 > 120.222 yes.
        var rig = Rig.Of(Timeframe.M1, "rising(vwap, 1)");

        rig.Run(TwoSessions()).ShouldBe("UFFUTFT");
    }

    [Fact]
    public void Crosses_above_the_open_of_the_day_is_unknown_on_the_first_bar_of_a_session()
    {
        // open of the day: 100 on Monday, 119 on Tuesday. Bar 1: unknown (nothing before it). 2: 98 > 100 no. 3: no.
        // Bar 4: today_open[1] does not exist in Tuesday: unknown. 5: 121 > 119, previous 122 <= 119 no. 6: 119 > 119 no.
        // Bar 7: 130 > 119 and the previous close 119 <= 119 (equal counts as not above yet): true.
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, today_open)");

        rig.Run(TwoSessions()).ShouldBe("UFFUFFT");
    }

    [Fact]
    public void The_crossing_written_out_with_both_sides_lagged_is_unknown_on_the_first_bar_too()
    {
        // close > vwap and close[1] <= vwap[1]: bar 1: 100 > 100 is false, so false. 2, 3: false. Bar 4: 122 > 120.667 is true and close[1] <= vwap[1] is unknown: unknown. 5: true and (122 <= 120.667 false) = false.
        // 6: 119 > 120.222 false. 7: 130 > 122.667 and 119 <= 120.222: true.
        var rig = Rig.Of(Timeframe.M1, "close > vwap and close[1] <= vwap[1]");

        rig.Run(TwoSessions()).ShouldBe("FFFUFFT");
    }

    [Fact]
    public void The_level_form_with_the_lag_on_the_close_only_is_true_on_the_gap_up_the_way_to_say_through_the_vwap_of_now()
    {
        // close > vwap and close[1] <= vwap: the lag is on the continuous close, the vwap is read now. Bar 1 false (100 > 100 no). 2, 3 false.
        // Bar 4: 122 > 120.667 and the last close of Monday, 96, <= 120.667: true. Bar 5: 121 > 120.833 and 122 <= 120.833 no: false. 6: false. 7: 130 > 122.667 and 119 <= 122.667: true.
        var rig = Rig.Of(Timeframe.M1, "close > vwap and close[1] <= vwap");

        rig.Run(TwoSessions()).ShouldBe("FFFTFFT");
    }

    [Fact]
    public void A_continuous_feature_runs_on_across_the_sessions_on_purpose()
    {
        // closes 100 98 96 | 122 121 119 130: rising(close, 1) is close > the previous close, whatever session that bar is in (ADR 0031: a moving average at 09:30 uses the previous day's bars).
        var rig = Rig.Of(Timeframe.M1, "rising(close, 1)");

        rig.Run(TwoSessions()).ShouldBe("UFFTFFT");
    }

    [Theory]
    [InlineData("crosses_above(close, vwap)")]
    [InlineData("rising(vwap, 3)")]
    [InlineData("close > vwap and close[1] <= vwap[1]")]
    [InlineData("crosses_above(close, today_open)")]
    [InlineData("crosses_above(close, vwap) and time >= \"09:17\"")]
    [InlineData("crosses_above(close, vwap) and time >= \"09:16\"")]
    public void None_of_these_needs_a_time_check_and_none_is_warned_about(string source)
    {
        foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1 })
        {
            Compiling.Of(source, timeframe).Problems.ShouldBeEmpty(source + " on " + timeframe.ToCode());
        }
    }

    [Fact]
    public void A_time_check_changes_nothing_about_the_first_bar_it_is_a_trading_rule_and_not_a_correctness_device()
    {
        // time at the close of bars 1 to 7: 09:16 09:17 09:18 | 09:16 09:17 09:18 09:19. crosses_above is U F F U F F T.
        // time >= 09:17 is F T T | F T T T: bar 1: U and F = F, bar 4: U and F = F; bar 7: T and T = T. So FFFFFFT.
        // time >= 09:16 is true throughout, so the word is the unguarded one: UFFUFFT (the unknown stays unknown: it was never a false alarm to guard against).
        Rig.Of(Timeframe.M1, "crosses_above(close, vwap) and time >= \"09:17\"").Run(TwoSessions()).ShouldBe("FFFFFFT");
        Rig.Of(Timeframe.M1, "crosses_above(close, vwap) and time >= \"09:16\"").Run(TwoSessions()).ShouldBe("UFFUFFT");
    }

    /// <summary>
    /// Three sessions of three bars (open, high, low, close); prev_day.high is Monday's 105 for Tuesday and Tuesday's 109 for Wednesday, and not ready on Monday.
    /// <code>
    /// bar:      1    2    3  |  4    5    6  |  7    8    9
    /// close:    101  104  99 |  102  107  108 |  114  115  111
    /// high:     102  105  104|  103  108  109 |  115  116  115
    /// prev_day.high:  -  -  - |  105  105  105 |  109  109  109
    /// </code>
    /// </summary>
    private static Bar[] ThreeSessions() =>
    [
        Clock.Bar(Clock.Mon, Clock.Open, 100, 102, 99, 101),
        Clock.Bar(Clock.Mon, Clock.Open + 1, 101, 105, 100, 104),
        Clock.Bar(Clock.Mon, Clock.Open + 2, 104, 104, 98, 99),
        Clock.Bar(Clock.Tue, Clock.Open, 101, 103, 100, 102),
        Clock.Bar(Clock.Tue, Clock.Open + 1, 102, 108, 101, 107),
        Clock.Bar(Clock.Tue, Clock.Open + 2, 107, 109, 106, 108),
        Clock.Bar(Clock.Wed, Clock.Open, 112, 115, 111, 114),
        Clock.Bar(Clock.Wed, Clock.Open + 1, 114, 116, 113, 115),
        Clock.Bar(Clock.Wed, Clock.Open + 2, 115, 115, 110, 111),
    ];

    [Fact]
    public void Crossing_the_previous_days_high_is_unknown_on_a_gap_up_first_bar_and_the_level_form_says_it()
    {
        // crosses_above(close, prev_day.high): Monday has no previous day, so unknown throughout. Tuesday: bar 4 prev_day.high[1] does not exist: unknown. Bar 5: 107 > 105 and the close before, 102, <= 105: true.
        // Bar 6: 108 > 105 but 107 <= 105 no: false. Wednesday: bar 7: 114 > 109 but prev_day.high[1] does not exist: unknown (this is the gap through yesterday's high). Bar 8: 115 > 109, 114 <= 109 no: false. 9: false.
        Rig.Of(Timeframe.M1, "crosses_above(close, prev_day.high)").Run(ThreeSessions()).ShouldBe("UUUUTFUFF");

        // the level form reads the previous day's high now and lags the close only: Monday unknown. Bar 4: 102 > 105 no: false. Bar 5: 107 > 105 and 102 <= 105: true. Bar 6: 108 > 105 and 107 <= 105 no: false.
        // Bar 7: 114 > 109 and Tuesday's last close, 108, <= 109: true, the gap through the level. Bars 8, 9: false.
        Rig.Of(Timeframe.M1, "close > prev_day.high and close[1] <= prev_day.high").Run(ThreeSessions()).ShouldBe("UUUFTFTFF");
    }

    /// <summary>Four days of two bars (09:15 and 15:29) with a daily close at 105, 121, 126 and 127 and an open at 100, 120, 130 and 124: gaps of 14.286, 7.438 and −1.587 percent.</summary>
    private static Bar[] FourDays()
    {
        var bars = new List<Bar>();
        (DateOnly Date, double Open, double High, double Low, double Close)[] days =
        [
            (Clock.Mon, 100, 110, 95, 105), (Clock.Tue, 120, 125, 118, 121), (Clock.Wed, 130, 131, 125, 126), (Clock.Thu, 124, 128, 120, 127),
        ];
        foreach (var (date, open, high, low, close) in days)
        {
            bars.Add(Clock.Bar(date, Clock.Open, open, high, low, open));
            bars.Add(Clock.Close(date, Clock.At(15, 29), close));
        }

        return [.. bars];
    }

    [Fact]
    public void On_a_daily_strategy_a_lag_is_by_position_so_yesterdays_gap_is_gap_pct_one_back()
    {
        // The daily lane closes one bar a day (at 15:29) and nothing restarts on it. After the closes of days 1 to 4 prev_day_close is -, 105, 121, 126 and gap_pct is -, 14.286, 7.438, -1.587.
        // gap_pct[1] > 10 reads the day before: unknown until day 3's close (the steps before it show the lane as of the last close), true at day 3 (14.286 > 10), false at day 4 (7.438).
        // Steps: d1 09:15, d1 close, d2 09:15, d2 close, d3 09:15, d3 close, d4 09:15, d4 close.
        Rig.Of(Timeframe.D1, "gap_pct[1] > 10").Run(FourDays()).ShouldBe("UUUUUTTF");

        // prev_day_close[1] == 105 reads two days back: day 3's close sees day 2's 105; day 4's sees 121.
        Rig.Of(Timeframe.D1, "prev_day_close[1] == 105").Run(FourDays()).ShouldBe("UUUUUTTF");
    }

    [Fact]
    public void The_same_lag_on_a_one_minute_strategy_is_unknown_on_the_first_bar_of_every_session()
    {
        // 1m: prev_day_close is -, 105, 121, 126 on days 1 to 4; at 09:15 of a day the value one bar back would be the day before's last: not in this session, so unknown. At 15:29 of day 2 and later it is the 09:15 value of the same day.
        // Steps: d1 09:15 (no previous day), d1 15:29, d2 09:15, d2 15:29, d3 09:15, d3 15:29, d4 09:15, d4 15:29.
        Rig.Of(Timeframe.M1, "prev_day_close[1] > 0").Run(FourDays()).ShouldBe("UUUTUTUT");
    }
}
