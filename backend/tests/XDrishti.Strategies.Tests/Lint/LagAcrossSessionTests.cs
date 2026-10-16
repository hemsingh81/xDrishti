using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Lint;

/// <summary>
/// <c>lag_across_session</c> (warning; ADR 0034, Amendment 19): a lagged read is by position and the frame's history runs on across sessions, so for a feature that starts again at the session open and has a value at
/// once, the first bars of every session read the previous session. The table of <see cref="LintCases"/> pins one message per shape; here are the rules behind it: which names, which timeframes and lags, the
/// <c>time</c> guard that clears it, and what the warning says about the guard it asks for. That the numbers are the frame's is shown by <see cref="Evaluation.LagAcrossSessionTruthTests"/>.
/// </summary>
public sealed partial class LagAcrossSessionTests
{
    /// <summary>The ten features that restart at the session open and have a value at once (written out; not derived from the catalog).</summary>
    private static readonly string[] SessionValues =
    [
        "vwap", "today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct",
    ];

    public static TheoryData<string> AllNames { get; } = [.. AllFeatureForms.Forms.Keys];

    [GeneratedRegex("`and time >= \"(\\d\\d:\\d\\d)\"`")]
    private static partial Regex GuardLiteral();

    private static string Reference(Timeframe feature, Timeframe evaluation) => feature == evaluation ? "vwap" : "tf(\"" + feature.ToCode() + "\").vwap";

    private static Problem[] Warned(string source, Timeframe timeframe) =>
        [.. Compiling.Of(source, timeframe).Problems.Where(p => p.Code == LintCodes.LagAcrossSession)];

    // ---- which features -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Only_the_ten_session_values_are_warned_about_when_they_are_lagged(string name)
    {
        var form = AllFeatureForms.Forms[name];

        var warnings = Warned("rising(" + form + ", 1)", Timeframe.M5);

        warnings.Length.ShouldBe(SessionValues.Contains(name) ? 1 : 0, name);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void No_feature_is_warned_about_when_it_is_not_lagged(string name)
    {
        var result = Compiling.Of(AllFeatureForms.ConditionOf(name), Timeframe.M5);

        result.Problems.ShouldNotContain(p => p.Code == LintCodes.LagAcrossSession, name);
    }

    [Fact]
    public void The_names_written_out_here_are_ten_of_the_catalogs_fifty_six()
    {
        AllFeatureForms.Forms.Count.ShouldBe(56);
        SessionValues.Distinct().Count().ShouldBe(10);
        SessionValues.ShouldAllBe(n => AllFeatureForms.Forms.ContainsKey(n));
    }

    // ---- which timeframes and lags ---------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The strategy timeframe E, the timeframe T of the feature and the lag L; whether the read crosses a session open at some evaluation, and the clock time from which every evaluation stays inside the session
    /// (09:15 plus (L + 1) · T minutes, at most 15:30; none when even the last bar of the session is within L bars of the open). Worked out by hand.
    /// </summary>
    public static TheoryData<Timeframe, Timeframe, int, string?, bool> Shapes => new()
    {
        // the strategy reads its own timeframe: the first evaluation is the first bar, so any lag crosses
        { Timeframe.M1, Timeframe.M1, 1, "09:17", true },
        { Timeframe.M5, Timeframe.M5, 1, "09:25", true },
        { Timeframe.M5, Timeframe.M5, 2, "09:30", true },
        { Timeframe.M15, Timeframe.M15, 1, "09:45", true },
        { Timeframe.M30, Timeframe.M30, 1, "10:15", true },
        { Timeframe.H1, Timeframe.H1, 1, "11:15", true },

        // a feature on a finer timeframe than the strategy: the first evaluation already has E / T bars of the session
        { Timeframe.M5, Timeframe.M1, 4, null, false },
        { Timeframe.M5, Timeframe.M1, 5, "09:21", true },
        { Timeframe.M10, Timeframe.M5, 1, null, false },
        { Timeframe.M10, Timeframe.M5, 2, "09:30", true },
        { Timeframe.M30, Timeframe.M10, 2, null, false },
        { Timeframe.M30, Timeframe.M10, 3, "09:55", true },
        { Timeframe.M15, Timeframe.M1, 14, null, false },
        { Timeframe.M15, Timeframe.M1, 15, "09:31", true },

        // a feature on a coarser timeframe than the strategy
        { Timeframe.M15, Timeframe.H1, 1, "11:15", true },
        { Timeframe.M5, Timeframe.M30, 1, "10:15", true },

        // the end of the session: the last bar may be short, and is the farthest from the open
        { Timeframe.H1, Timeframe.H1, 5, "15:15", true },
        { Timeframe.H1, Timeframe.H1, 6, "15:30", true },
        { Timeframe.H1, Timeframe.H1, 7, "none", true },
        { Timeframe.M30, Timeframe.M30, 11, "15:15", true },
        { Timeframe.M30, Timeframe.M30, 12, "15:30", true },
        { Timeframe.M30, Timeframe.M30, 13, "none", true },
        { Timeframe.M15, Timeframe.M15, 24, "15:30", true },
        { Timeframe.M15, Timeframe.M15, 25, "none", true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_lag_is_warned_about_when_the_first_evaluation_comes_before_the_read_stays_inside_the_session(Timeframe evaluation, Timeframe feature, int lag, string? clearsAt, bool warned)
    {
        var source = "rising(" + Reference(feature, evaluation) + ", " + lag + ")";

        var warnings = Warned(source, evaluation);

        warnings.Length.ShouldBe(warned ? 1 : 0, source + " on " + evaluation.ToCode());
        if (!warned)
        {
            return;
        }

        var message = warnings[0].Message;
        if (clearsAt == "none")
        {
            GuardLiteral().IsMatch(message).ShouldBeFalse(message);
            message.ShouldEndWith("No bar of a session is far enough from its open for a read that far back; read fewer bars back.");
        }
        else
        {
            GuardLiteral().Match(message).Groups[1].Value.ShouldBe(clearsAt, message);
        }
    }

    [Theory]
    [InlineData(Timeframe.D1)]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void A_strategy_on_a_daily_or_longer_timeframe_evaluates_at_the_end_of_a_session_and_is_not_warned(Timeframe evaluation)
    {
        // today_open, prev_day_close and gap_pct exist on 1D; on 1W and 1M the session values are refused, and a feature read on 1m by tf() is the one that could be lagged
        var source = evaluation == Timeframe.D1 ? "rising(today_open, 3) or crosses_above(close, prev_day.high)" : "rising(tf(\"1m\").vwap, 3)";

        Warned(source, evaluation).ShouldBeEmpty();
    }

    [Fact]
    public void A_deeper_lag_never_removes_a_warning_a_shallower_one_has()
    {
        foreach (var evaluation in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1 })
        {
            foreach (var feature in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1 })
            {
                var warnedBefore = false;
                for (var lag = 1; lag <= 100; lag++)
                {
                    var warned = Warned("rising(" + Reference(feature, evaluation) + ", " + lag + ")", evaluation).Length == 1;
                    (warnedBefore && !warned).ShouldBeFalse($"E {evaluation.ToCode()}, T {feature.ToCode()}: warned at lag {lag - 1} and not at {lag}");
                    warnedBefore |= warned;
                }

                warnedBefore.ShouldBeTrue($"E {evaluation.ToCode()}, T {feature.ToCode()}: a lag of 100 bars of the feature is more than the 60 minutes of the longest first evaluation, whatever the timeframes");
            }
        }
    }

    // ---- the guard -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Timeframe.M15, ">=", "09:15", false)]
    [InlineData(Timeframe.M15, ">=", "09:16", false)]
    [InlineData(Timeframe.M15, ">=", "09:30", false)]
    [InlineData(Timeframe.M15, ">=", "09:31", true)]
    [InlineData(Timeframe.M15, ">=", "09:45", true)]
    [InlineData(Timeframe.M15, ">", "09:15", false)]
    [InlineData(Timeframe.M15, ">", "09:29", false)]
    [InlineData(Timeframe.M15, ">", "09:30", true)]
    [InlineData(Timeframe.M15, ">", "09:45", true)]
    [InlineData(Timeframe.M1, ">=", "09:16", false)]
    [InlineData(Timeframe.M1, ">=", "09:17", true)]
    [InlineData(Timeframe.M1, ">", "09:16", true)]
    [InlineData(Timeframe.H1, ">=", "10:15", false)]
    [InlineData(Timeframe.H1, ">=", "10:16", true)]
    [InlineData(Timeframe.H1, ">", "10:15", true)]
    public void A_time_check_clears_the_warning_when_the_first_evaluation_it_lets_through_is_clean(Timeframe evaluation, string op, string time, bool cleared)
    {
        var source = "crosses_above(close, vwap) and time " + op + " \"" + time + "\"";

        Warned(source, evaluation).Length.ShouldBe(cleared ? 0 : 1, source + " on " + evaluation.ToCode());
    }

    [Theory]
    [InlineData("crosses_above(close, vwap) and time >= \"09:45\"", 0)]
    [InlineData("time >= \"09:45\" and crosses_above(close, vwap)", 0)]
    [InlineData("rsi(14) > 55 and time >= \"09:45\" and ema(20) > ema(50) and crosses_above(close, vwap)", 0)]
    [InlineData("(rsi(14) > 55 or ema(20) > ema(50)) and crosses_above(close, vwap) and time >= \"09:45\"", 0)]
    [InlineData("crosses_above(close, vwap) and time >= \"09:30\" and time >= \"09:45\"", 0)]
    [InlineData("crosses_above(close, vwap) and (time >= \"09:45\" and rsi(14) > 55)", 0)]
    [InlineData("crosses_above(close, vwap) or time >= \"09:45\"", 1)]
    [InlineData("not (time >= \"09:45\") and crosses_above(close, vwap)", 1)]
    [InlineData("crosses_above(close, vwap) and not (time < \"09:45\")", 1)]
    [InlineData("(crosses_above(close, vwap) and rsi(14) > 55) or (time >= \"09:45\" and rsi(14) > 60)", 1)]
    [InlineData("crosses_above(close, vwap) and time == \"09:45\"", 1)]
    [InlineData("crosses_above(close, vwap) and time <= \"15:00\"", 1)]
    [InlineData("crosses_above(close, vwap) and time < \"15:00\"", 1)]
    [InlineData("crosses_above(close, vwap) and time != \"09:30\"", 1)]
    public void Only_a_lower_bound_of_the_time_in_the_chain_of_ands_clears_it(string source, int warnings)
    {
        Warned(source, Timeframe.M15).Length.ShouldBe(warnings, source);
    }

    [Fact]
    public void A_check_of_the_time_clears_only_the_reads_it_is_late_enough_for()
    {
        var warnings = Warned("rising(vwap, 1) and rising(today_low, 3) and rising(today_open, 8) and time >= \"10:15\"", Timeframe.M15);

        // 10:15 is 60 minutes after the open: one bar back needs 30, three bars back 60, eight bars back 135
        warnings.Select(w => w.Message[..w.Message.IndexOf(" reads", StringComparison.Ordinal)]).ShouldBe(["`rising(today_open, 8)`"]);
    }

    // ---- what the warning is --------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void It_is_a_warning_with_no_suggestion_and_the_condition_still_compiles()
    {
        var result = Compiling.Of("crosses_above(close, vwap)");

        result.IsSuccess.ShouldBeTrue(Compiling.Describe(result));
        var warning = result.Condition!.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe("strategy.lint.lag_across_session");
        warning.Severity.ShouldBe(ProblemSeverity.Warning);
        warning.Suggestion.ShouldBeNull();
        (warning.Span.Start, warning.Span.End).ShouldBe((0, 26));
        result.Condition.HistoryDepth.ShouldBe(2);
        result.Condition.Features.Count.ShouldBe(2);
    }

    [Fact]
    public void A_condition_with_the_guard_has_no_warnings_and_the_same_plan()
    {
        var bare = Compiling.Condition("crosses_above(close, vwap)");
        var guarded = Compiling.Condition("crosses_above(close, vwap) and time >= \"09:45\"");

        guarded.Warnings.ShouldBeEmpty();
        guarded.Features.ShouldBe(bare.Features);
        guarded.HistoryDepth.ShouldBe(bare.HistoryDepth);
    }

    [Fact]
    public void One_call_gives_one_warning_naming_each_feature_once_in_the_order_written()
    {
        var warning = Warned("rising(vwap + today_open - vwap * today_low, 2)", Timeframe.M15).ShouldHaveSingleItem();

        warning.Message.ShouldStartWith("`rising(vwap + today_open - vwap * today_low, 2)` reads `vwap`, `today_open`, `today_low` up to 2 closed bars back");
    }

    [Fact]
    public void The_features_that_are_not_session_values_are_left_out_of_the_names()
    {
        var warning = Warned("crosses_above(close * ema(20), vwap * rsi(14))", Timeframe.M15).ShouldHaveSingleItem();

        warning.Message.ShouldStartWith("`crosses_above(close * ema(20), vwap * rsi(14))` reads `vwap` one closed bar back");
    }

    [Fact]
    public void The_deepest_read_of_the_call_is_the_one_named_and_the_latest_guard_is_the_one_asked_for()
    {
        var warning = Warned("rising(vwap + today_open[3], 2)", Timeframe.M15).ShouldHaveSingleItem();

        // vwap is read 2 bars back, today_open 5: the guard must clear the 5 (6 bars of 15 minutes after the open: 10:45)
        warning.Message.ShouldStartWith("`rising(vwap + today_open[3], 2)` reads `vwap`, `today_open` up to 5 closed bars back");
        GuardLiteral().Match(warning.Message).Groups[1].Value.ShouldBe("10:45");
    }

    [Fact]
    public void A_lagged_read_in_any_position_of_the_condition_is_found()
    {
        Warned("not crosses_above(close, vwap)", Timeframe.M15).Length.ShouldBe(1);
        Warned("rsi(14) > 55 and (ema(20) > ema(50) or between(close, vwap[1], 100))", Timeframe.M15).Length.ShouldBe(1);
        Warned("min(vwap[1], close) > abs(today_open[2])", Timeframe.M15).Length.ShouldBe(2);
        Warned("crosses_above(close, vwap) and crosses_below(close, vwap)", Timeframe.M15).Length.ShouldBe(2);
    }

    [Fact]
    public void A_lagged_call_that_could_not_be_bound_still_gets_it_so_that_fixing_the_other_fault_does_not_bring_it_in()
    {
        var result = Compiling.Of("crosses_above(close + true, vwap)");

        result.IsSuccess.ShouldBeFalse();
        result.Problems.Select(p => p.Code).ShouldBe([LintCodes.LagAcrossSession, LintCodes.TypeMismatch]);
        Compiling.Of("crosses_above(close, vwap)").Problems.Select(p => p.Code).ShouldBe([LintCodes.LagAcrossSession]);
    }

    [Fact]
    public void A_count_given_by_a_parameter_is_read_as_the_number_it_is()
    {
        var result = Compiling.Of("rising(vwap, n)", Timeframe.M5, Compiling.Params(("n", 3)));

        var warning = result.Problems.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LintCodes.LagAcrossSession);
        warning.Message.ShouldStartWith("`rising(vwap, n)` reads `vwap` up to 3 closed bars back");
        GuardLiteral().Match(warning.Message).Groups[1].Value.ShouldBe("09:35");
    }
}
