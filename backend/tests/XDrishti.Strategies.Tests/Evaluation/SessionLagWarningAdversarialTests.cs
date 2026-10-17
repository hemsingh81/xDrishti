using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// ISS-13, adversarial, the narrowed warning <c>lag_across_session</c>: a read of a per-session feature on an intraday lane warns exactly when its effective lag <c>L</c> (its own subscript plus the lags the enclosing
/// <c>crosses_*</c> / <c>rising</c> / <c>falling</c> adds) is such that <c>L · T</c> reaches the 375 minutes of a session, <c>T</c> the minutes of the feature's own timeframe: L = 375, 75, 38, 25, 13, 7 on 1m, 5m, 10m, 15m,
/// 30m, 1h. The strategy's own timeframe and a <c>time</c> check do not matter, lag 0 and the continuous or daily features never warn. A lag above the frame's 255 is also an error; the warning is looked for by its code.
/// </summary>
public sealed class SessionLagWarningAdversarialTests
{
    private static Problem[] Warnings(string source, Timeframe strategy) =>
        [.. Compiling.Of(source, strategy).Problems.Where(p => p.Code == LintCodes.LagAcrossSession)];

    public static TheoryData<Timeframe, int, int> SessionBoundaries => new()
    {
        { Timeframe.M1, 375, 375 },
        { Timeframe.M5, 75, 75 },
        { Timeframe.M10, 38, 38 },
        { Timeframe.M15, 25, 25 },
        { Timeframe.M30, 13, 13 },
        { Timeframe.H1, 7, 7 },
    };

    [Theory]
    [MemberData(nameof(SessionBoundaries))]
    public void A_subscript_warns_from_the_first_lag_that_a_whole_session_cannot_hold_and_not_one_bar_before(Timeframe timeframe, int firstWarned, int barsInSession)
    {
        var before = $"vwap[{firstWarned - 1}] > 0";
        var at = $"vwap[{firstWarned}] > 0";

        Warnings(before, timeframe).ShouldBeEmpty(before + " on " + timeframe);
        var warning = Warnings(at, timeframe).ShouldHaveSingleItem(at + " on " + timeframe);
        warning.Severity.ShouldBe(ProblemSeverity.Warning);
        warning.Message.ShouldContain($"{firstWarned} closed bars back");
        warning.Message.ShouldContain($"at most {barsInSession} bars of {timeframe.ToCode()}");
        warning.Message.ShouldContain($"Read at most {barsInSession - 1} bars back");
        at[warning.Span.Start..warning.Span.End].ShouldBe($"vwap[{firstWarned}]", "the span is the subscripted feature");
    }

    [Theory]
    [MemberData(nameof(SessionBoundaries))]
    public void The_lag_of_a_crossing_or_a_run_adds_to_the_subscript_before_the_session_is_counted(Timeframe timeframe, int firstWarned, int barsInSession)
    {
        _ = barsInSession;
        var t = timeframe.ToCode();
        var p = timeframe == Timeframe.M1 ? string.Empty : $"tf(\"{t}\").";

        // crosses_above reads the second side one bar further back than its subscript: vwap[n - 2] is read at n - 1 (clean), vwap[n - 1] at n (warned)
        Warnings($"crosses_above({p}close, {p}vwap[{firstWarned - 2}])", timeframe).ShouldBeEmpty($"crosses_above reading vwap at lag {firstWarned - 1} on {t}");
        Warnings($"crosses_above({p}close, {p}vwap[{firstWarned - 1}])", timeframe).ShouldHaveSingleItem($"crosses_above reading vwap at lag {firstWarned} on {t}");

        // rising(x, n) reads x at lags 0 to n; with a subscript they add
        Warnings($"rising({p}vwap, {firstWarned - 1})", timeframe).ShouldBeEmpty($"rising(vwap, {firstWarned - 1}) on {t}");
        Warnings($"rising({p}vwap, {firstWarned})", timeframe).ShouldHaveSingleItem($"rising(vwap, {firstWarned}) on {t}");
        Warnings($"falling({p}today_open, {firstWarned})", timeframe).ShouldHaveSingleItem($"falling(today_open, {firstWarned}) on {t}");
        Warnings($"rising({p}vwap[1], {firstWarned - 2})", timeframe).ShouldBeEmpty($"rising(vwap[1], {firstWarned - 2}) reads lag {firstWarned - 1}");
        Warnings($"rising({p}vwap[1], {firstWarned - 1})", timeframe).ShouldHaveSingleItem($"rising(vwap[1], {firstWarned - 1}) reads lag {firstWarned}");
    }

    [Theory]
    [InlineData("vwap > 0", Timeframe.M15)]
    [InlineData("vwap[1] > 0", Timeframe.M15)]
    [InlineData("crosses_above(close, vwap)", Timeframe.M15)]
    [InlineData("rising(vwap, 3)", Timeframe.M1)]
    [InlineData("rising(vwap, 6)", Timeframe.H1)]
    [InlineData("tf(\"1h\").vwap[6] > 0", Timeframe.D1)]
    [InlineData("tf(\"5m\").vwap[74] > 0", Timeframe.D1)]
    [InlineData("tf(\"1D\").gap_pct[400] > 0", Timeframe.M15)]
    [InlineData("today_open[400] > 0", Timeframe.D1)]
    [InlineData("gap_pct[2] > 0", Timeframe.D1)]
    [InlineData("ema(20)[200] > 0", Timeframe.M15)]
    [InlineData("sma(20)[200] > 0", Timeframe.M1)]
    [InlineData("close[200] > 0", Timeframe.M1)]
    [InlineData("crosses_above(close, or_high(30))", Timeframe.M15)]
    [InlineData("day_of_week[1] == MON", Timeframe.M1)]
    public void A_read_inside_the_session_a_lag_zero_a_continuous_feature_and_a_daily_lane_never_warn(string source, Timeframe timeframe)
    {
        Warnings(source, timeframe).ShouldBeEmpty(source + " on " + timeframe);
    }

    [Theory]
    [InlineData("tf(\"5m\").vwap[75] > 0")]
    [InlineData("tf(\"5m\").vwap[80] > 0")]
    [InlineData("tf(\"1h\").today_open[7] > 0")]
    [InlineData("tf(\"15m\").prev_day_close[25] > 0")]
    [InlineData("tf(\"30m\").gap_pct[13] > 0")]
    [InlineData("tf(\"10m\").prev_day_high[38] > 0")]
    public void A_daily_strategy_is_warned_about_a_read_of_an_intraday_lane_that_lands_in_an_earlier_session(string source)
    {
        // the strategy timeframe does not matter: the read is of the lane's history
        Warnings(source, Timeframe.D1).ShouldHaveSingleItem(source + " on 1D");
    }

    [Theory]
    [InlineData("vwap[25] > 0", true)]
    [InlineData("vwap[24] > 0", false)]
    [InlineData("vwap[25] > 0 and time >= \"10:00\"", true)]
    [InlineData("vwap[25] > 0 and time >= \"15:00\"", true)]
    [InlineData("vwap[24] > 0 and time >= \"10:00\"", false)]
    [InlineData("time >= \"10:00\" and rising(vwap, 25)", true)]
    [InlineData("time >= \"10:00\" and rising(vwap, 24)", false)]
    [InlineData("(time >= \"10:00\" or vwap[25] > 0)", true)]
    public void A_time_check_neither_adds_nor_removes_the_warning(string source, bool warned)
    {
        Warnings(source, Timeframe.M15).Length.ShouldBe(warned ? 1 : 0, source);
    }

    [Fact]
    public void One_call_that_reads_several_features_from_too_far_back_is_one_warning_that_names_them_all()
    {
        var source = "crosses_above(vwap[30], today_open[30])";

        var warning = Warnings(source, Timeframe.M15).ShouldHaveSingleItem();

        warning.Message.ShouldContain("`vwap`");
        warning.Message.ShouldContain("`today_open`");
        source[warning.Span.Start..warning.Span.End].ShouldBe(source, "the span is the call");
    }

    [Theory]
    [InlineData("rising(vwap, 30) and rising(today_open, 30)", 2)]
    [InlineData("vwap[30] > 0 and vwap[40] > 0", 2)]
    [InlineData("vwap[30] + today_open[30] > 0", 2)]
    [InlineData("rising(vwap, 30) and rising(vwap, 30)", 2)]
    [InlineData("rising(vwap[30], 1)", 1)]
    public void The_warning_is_given_once_for_each_call_or_lone_subscripted_feature(string source, int count)
    {
        Warnings(source, Timeframe.M15).Length.ShouldBe(count, source);
    }

    public static TheoryData<Timeframe, string> LanesAndNames
    {
        get
        {
            var data = new TheoryData<Timeframe, string>();
            foreach (var lane in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1 })
            {
                foreach (var name in new[] { "vwap", "today_open", "prev_day_close", "gap_pct" })
                {
                    data.Add(lane, name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LanesAndNames))]
    public void The_warning_is_given_exactly_where_the_real_frame_has_no_value_at_the_close_of_a_full_second_session(Timeframe lane, string name)
    {
        // The frame side: two whole sessions of 375 bars (volume 10 on every bar, so the vwap is ready from the first bar), depth 256; at the close of the second the lane has closed ceil(375 / T) bars of it, and the
        // value L bars back exists exactly for L below that. The lint side: the warning for name[L]. They must agree at, around and far from the boundary (the frame keeps at most lag 255).
        var length = lane.Minutes();
        var barsInSession = (375 + length - 1) / length;
        var frame = Frames.Of(new FrameOptions(HistoryDepth: 256), new FeatureSpec(name, lane));
        for (var d = 0; d < 2; d++)
        {
            for (var m = 0; m < 375; m++)
            {
                frame.Step(Clock.Close(d == 0 ? Clock.Mon : Clock.Tue, Clock.Open + m, 100 + d * 7 + m % 13 * 0.5 + m * 0.01));
            }
        }

        var prefix = lane == Timeframe.M1 ? string.Empty : $"tf(\"{lane.ToCode()}\").";
        var lags = new[] { 1, barsInSession - 2, barsInSession - 1, barsInSession, barsInSession + 1, 100, 255 }.Where(l => l is >= 1 and <= 255).Distinct().Order();
        foreach (var lag in lags)
        {
            var noValue = double.IsNaN(frame.At(0, lag));
            var warned = Warnings($"{prefix}{name}[{lag}] > 0", lane).Length == 1;

            warned.ShouldBe(noValue, $"{name}[{lag}] on {lane.ToCode()}: the frame has {(noValue ? "no value" : "a value")} that far back at the close of a session of {barsInSession} bars");
            (lag >= barsInSession).ShouldBe(noValue, $"{name}[{lag}] on {lane.ToCode()}: independent arithmetic (a session holds {barsInSession} bars)");
        }
    }

    [Fact]
    public void A_read_that_is_too_deep_for_the_frame_is_an_error_and_the_warning_still_comes_with_it()
    {
        // 1m: lag 375 needs a history of 376, above the frame's 256: lag_too_deep (error). The same read can never be known: the warning is given too.
        var result = Compiling.Of("vwap[375] > 0", Timeframe.M1);

        result.IsSuccess.ShouldBeFalse();
        result.Problems.ShouldContain(p => p.Code == LintCodes.LagTooDeep && p.Severity == ProblemSeverity.Error);
        result.Problems.ShouldContain(p => p.Code == LintCodes.LagAcrossSession && p.Severity == ProblemSeverity.Warning);
    }

    [Fact]
    public void A_warning_alone_does_not_stop_the_condition_from_compiling_and_the_condition_is_never_true()
    {
        // 15m: vwap[25] is a read 25 bars back; the frame can hold it (depth 26), the condition compiles, and over three whole sessions it is never true — vwap[25] is unknown for every bar of a 25-bar session.
        var rig = Rig.Of(Timeframe.M15, "vwap[25] > 0 or vwap[30] > 0");
        var truths = Series.Sessions(3, 3).Select(bar => rig.Step(bar)[0]).ToArray();

        Compiling.Of("vwap[25] > 0", Timeframe.M15).IsSuccess.ShouldBeTrue();
        truths.ShouldAllBe(t => t == Truth.Unknown);
    }
}
