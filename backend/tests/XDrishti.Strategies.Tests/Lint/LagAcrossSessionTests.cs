using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Lint;

/// <summary>
/// <c>lag_across_session</c> (warning; ADR 0035, narrowing ADR 0034 Amendment 19): the frame keeps the history of a per-session feature for the current session only, so a lagged read never compares two sessions and
/// needs no help from the condition. What the lint still says is that a read from <i>further back than a whole session holds</i> is never known: <c>k</c> bars of timeframe <c>T</c> back with <c>k · T</c> at least
/// the 375 minutes of a session (<c>vwap[75]</c> on 5m, <c>rising(vwap, 7)</c> on 1h). The table of <see cref="LintCases"/> pins one message per shape; here are the rules behind it: which names, which timeframes and
/// lags, that the strategy's own timeframe and a <c>time</c> check make no difference, and what the warning says. That the numbers are the frame's is shown by <see cref="Evaluation.SessionLagTruthTests"/>.
/// </summary>
public sealed class LagAcrossSessionTests
{
    /// <summary>The twelve features that restart each session (written out; not derived from the catalog).</summary>
    private static readonly string[] Restarting =
    [
        "vwap", "or_high", "or_low", "today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct",
    ];

    private static readonly Timeframe[] Intraday = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    public static TheoryData<string> AllNames { get; } = [.. AllFeatureForms.Forms.Keys];

    /// <summary>The bars a session of the timeframe holds (375 minutes, the last bar may be short), written by hand: 375, 75, 38, 25, 13, 7.</summary>
    private static int BarsInSession(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => 375,
        Timeframe.M5 => 75,
        Timeframe.M10 => 38,
        Timeframe.M15 => 25,
        Timeframe.M30 => 13,
        Timeframe.H1 => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe)),
    };

    private static string Reference(Timeframe feature, Timeframe evaluation) => feature == evaluation ? "vwap" : "tf(\"" + feature.ToCode() + "\").vwap";

    private static Problem[] Warned(string source, Timeframe timeframe, IReadOnlyDictionary<string, double>? parameters = null) =>
        [.. Compiling.Of(source, timeframe, parameters).Problems.Where(p => p.Code == LintCodes.LagAcrossSession)];

    // ---- which features -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Only_the_twelve_restarting_features_are_warned_about_when_they_are_read_from_further_back_than_a_session_holds(string name)
    {
        var form = AllFeatureForms.Forms[name];

        var warnings = Warned("rising(" + form + ", 75)", Timeframe.M5);

        warnings.Length.ShouldBe(Restarting.Contains(name) ? 1 : 0, name);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void No_feature_is_warned_about_one_bar_short_of_a_session(string name)
    {
        var form = AllFeatureForms.Forms[name];

        Warned("rising(" + form + ", 74)", Timeframe.M5).ShouldBeEmpty(name);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void No_feature_is_warned_about_when_it_is_not_lagged(string name)
    {
        var result = Compiling.Of(AllFeatureForms.ConditionOf(name), Timeframe.M5);

        result.Problems.ShouldNotContain(p => p.Code == LintCodes.LagAcrossSession, name);
    }

    [Fact]
    public void The_names_written_out_here_are_twelve_of_the_catalogs_fifty_six()
    {
        AllFeatureForms.Forms.Count.ShouldBe(56);
        Restarting.Distinct().Count().ShouldBe(12);
        Restarting.ShouldAllBe(n => AllFeatureForms.Forms.ContainsKey(n));
    }

    // ---- which timeframes and lags ---------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Timeframe of the feature and the lag at the boundary: the last lag that is not warned and the first that is (k · T below and at 375 minutes). Worked out by hand: 74/75, 37/38, 24/25, 12/13, 6/7.</summary>
    public static TheoryData<Timeframe, int> Boundaries => new()
    {
        { Timeframe.M5, 74 },
        { Timeframe.M10, 37 },
        { Timeframe.M15, 24 },
        { Timeframe.M30, 12 },
        { Timeframe.H1, 6 },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void A_read_one_bar_short_of_a_session_is_clean_and_the_next_bar_back_is_warned_on_any_strategy_timeframe(Timeframe feature, int lastClean)
    {
        foreach (var evaluation in new[] { feature, Timeframe.M15, Timeframe.H1, Timeframe.D1 })
        {
            var clean = "rising(" + Reference(feature, evaluation) + ", " + lastClean + ")";
            var dead = "rising(" + Reference(feature, evaluation) + ", " + (lastClean + 1) + ")";

            Warned(clean, evaluation).ShouldBeEmpty(clean + " on " + evaluation.ToCode());
            Warned(dead, evaluation).Length.ShouldBe(1, dead + " on " + evaluation.ToCode());
        }
    }

    [Fact]
    public void A_one_minute_read_is_never_warned_below_the_depth_the_frame_keeps_and_is_warned_from_375_where_it_is_also_too_deep()
    {
        Warned("rising(vwap, 255)", Timeframe.M1).ShouldBeEmpty("255 minutes are inside a session of 375");

        var beyond = Compiling.Of("close > vwap[375]", Timeframe.M1);

        beyond.Problems.Select(p => p.Code).ShouldBe([LintCodes.LagTooDeep, LintCodes.LagAcrossSession], "a read of 375 bars is both too deep for the frame and a whole session back");
        Warned("close > vwap[374]", Timeframe.M1).ShouldBeEmpty("374 minutes are inside a session of 375 (the frame keeps 256, so it is too deep, but not warned about twice)");
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M10)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.M30)]
    [InlineData(Timeframe.H1)]
    public void The_warning_follows_the_arithmetic_of_the_lag_and_the_minutes_of_the_features_timeframe_at_every_lag(Timeframe feature)
    {
        var minutes = feature.Minutes();

        for (var lag = 1; lag <= 255; lag++)
        {
            var expected = lag * minutes >= SessionClock.RegularBars;

            Warned("rising(" + Reference(feature, feature) + ", " + lag + ")", feature).Length.ShouldBe(expected ? 1 : 0, $"{feature.ToCode()}, lag {lag}");
        }
    }

    [Theory]
    [InlineData(Timeframe.D1, "tf(\"5m\").vwap[80] > 0", 1)]
    [InlineData(Timeframe.D1, "tf(\"1h\").today_open[7] > 0", 1)]
    [InlineData(Timeframe.D1, "tf(\"1h\").today_open[6] > 0", 0)]
    [InlineData(Timeframe.D1, "rising(tf(\"15m\").prev_day_high, 25)", 1)]
    [InlineData(Timeframe.W1, "tf(\"30m\").gap_pct[13] > 0", 1)]
    [InlineData(Timeframe.M5, "tf(\"1h\").vwap[7] > 0", 1)]
    [InlineData(Timeframe.H1, "tf(\"5m\").vwap[75] > 0", 1)]
    [InlineData(Timeframe.H1, "tf(\"5m\").vwap[74] > 0", 0)]
    public void The_strategys_own_timeframe_does_not_matter_a_read_of_a_lane_is_judged_by_the_lane(Timeframe evaluation, string source, int warnings)
    {
        Warned(source, evaluation).Length.ShouldBe(warnings, source + " on " + evaluation.ToCode());
    }

    [Theory]
    [InlineData(Timeframe.D1, "rising(today_open, 200)")]
    [InlineData(Timeframe.D1, "gap_pct[200] > 0")]
    [InlineData(Timeframe.D1, "prev_day.close[200] > 0")]
    [InlineData(Timeframe.M15, "tf(\"1D\").gap_pct[200] > 0")]
    [InlineData(Timeframe.M5, "rising(tf(\"1D\").today_open, 200)")]
    [InlineData(Timeframe.H1, "tf(\"1D\").prev_day_close[255] > 0")]
    public void A_read_of_a_restarting_feature_on_the_daily_lane_is_by_position_and_is_never_warned(Timeframe evaluation, string source)
    {
        Warned(source, evaluation).ShouldBeEmpty(source + " on " + evaluation.ToCode());
    }

    [Fact]
    public void A_crossing_adds_one_lag_to_the_subscript_and_rising_adds_its_count()
    {
        // crosses_above(close, vwap[k]) reads the vwap k and k + 1 bars back; rising(vwap[k], n) reads k to k + n
        Warned("crosses_above(close, vwap[74])", Timeframe.M5).Length.ShouldBe(1, "74 + 1 = 75 bars of 5m are 375 minutes");
        Warned("crosses_above(close, vwap[73])", Timeframe.M5).ShouldBeEmpty("73 + 1 = 74 bars of 5m are 370 minutes");
        Warned("rising(vwap[20], 5)", Timeframe.M15).Length.ShouldBe(1, "20 + 5 = 25 bars of 15m are 375 minutes");
        Warned("rising(vwap[20], 4)", Timeframe.M15).ShouldBeEmpty("20 + 4 = 24 bars of 15m are 360 minutes");
        Warned("falling(vwap[10], 15)", Timeframe.M15).Length.ShouldBe(1, "10 + 15 = 25");
    }

    [Fact]
    public void A_lag_that_fits_a_session_but_is_large_is_not_warned_it_is_unknown_for_the_first_bars_only()
    {
        // 60 bars of 1m is 60 minutes into the session: unknown for the first 60 minutes, known after; the lint says nothing, the frame says so (SessionLagTruthTests)
        Warned("rising(vwap, 60)", Timeframe.M1).ShouldBeEmpty();
        Warned("rising(vwap, 20)", Timeframe.M15).ShouldBeEmpty();
    }

    // ---- a time check changes nothing ------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("rising(vwap, 25)", 1)]
    [InlineData("rising(vwap, 24)", 0)]
    [InlineData("crosses_above(close, vwap)", 0)]
    [InlineData("rising(today_open, 25) or rising(vwap, 3)", 1)]
    public void A_time_check_neither_adds_nor_removes_the_warning(string source, int warnings)
    {
        foreach (var variant in new[]
        {
            source,
            "(" + source + ") and time >= \"09:45\"",
            "time >= \"09:30\" and (" + source + ")",
            "(" + source + ") and time >= \"15:00\"",
            "(" + source + ") or time >= \"09:45\"",
            "(" + source + ") and not (time < \"10:00\")",
            "(" + source + ") and time <= \"14:00\"",
        })
        {
            Warned(variant, Timeframe.M15).Length.ShouldBe(warnings, variant);
        }
    }

    [Fact]
    public void The_headline_examples_compile_with_no_problem_at_all_with_or_without_a_time_check()
    {
        foreach (var timeframe in Intraday)
        {
            foreach (var source in new[]
            {
                "crosses_above(close, vwap)",
                "crosses_below(close, vwap)",
                "close > vwap and close[1] <= vwap[1]",
                "rising(vwap, 3)",
                "crosses_above(close, today_open)",
                "crosses_above(close, prev_day.high)",
                "crosses_above(close, vwap) and time >= \"09:45\"",
                "crosses_above(close, vwap) and time >= \"09:16\"",
            })
            {
                Compiling.Of(source, timeframe).Problems.ShouldBeEmpty(source + " on " + timeframe.ToCode());
            }
        }
    }

    // ---- what the warning is --------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void It_is_a_warning_with_no_suggestion_and_the_condition_still_compiles()
    {
        var result = Compiling.Of("rising(vwap, 25)");

        result.IsSuccess.ShouldBeTrue(Compiling.Describe(result));
        var warning = result.Condition!.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe("strategy.lint.lag_across_session");
        warning.Severity.ShouldBe(ProblemSeverity.Warning);
        warning.Suggestion.ShouldBeNull();
        (warning.Span.Start, warning.Span.End).ShouldBe((0, 16));
        result.Condition.HistoryDepth.ShouldBe(26);
        result.Condition.Features.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(Timeframe.M5, 75, 74)]
    [InlineData(Timeframe.M10, 38, 37)]
    [InlineData(Timeframe.M15, 25, 24)]
    [InlineData(Timeframe.M30, 13, 12)]
    [InlineData(Timeframe.H1, 7, 6)]
    public void The_message_says_how_many_bars_a_session_holds_and_how_far_back_to_read(Timeframe feature, int holds, int atMost)
    {
        BarsInSession(feature).ShouldBe(holds);

        var warning = Warned("rising(vwap, " + holds + ")", feature).ShouldHaveSingleItem();

        warning.Message.ShouldBe(
            $"`rising(vwap, {holds})` reads `vwap` up to {holds} closed bars back, but a session holds at most {holds} bars of {feature.ToCode()} and a per-session feature has no value from an earlier session: "
            + $"that part of the condition is never known, and unknown is not true. Read at most {atMost} bars back.");
    }

    [Fact]
    public void One_call_gives_one_warning_naming_each_feature_once_in_the_order_written()
    {
        var warning = Warned("rising(vwap + today_open - vwap * today_low, 25)", Timeframe.M15).ShouldHaveSingleItem();

        warning.Message.ShouldStartWith("`rising(vwap + today_open - vwap * today_low, 25)` reads `vwap`, `today_open`, `today_low` up to 25 closed bars back");
    }

    [Fact]
    public void The_features_that_are_not_restarting_ones_are_left_out_of_the_names()
    {
        var warning = Warned("rising(close * ema(20) + vwap * rsi(14), 25)", Timeframe.M15).ShouldHaveSingleItem();

        warning.Message.ShouldStartWith("`rising(close * ema(20) + vwap * rsi(14), 25)` reads `vwap` up to 25 closed bars back");
    }

    [Fact]
    public void The_deepest_read_of_the_call_is_the_one_named()
    {
        var warning = Warned("rising(vwap + today_open[3], 25)", Timeframe.M15).ShouldHaveSingleItem();

        // vwap is read 25 bars back, today_open 28
        warning.Message.ShouldStartWith("`rising(vwap + today_open[3], 25)` reads `vwap`, `today_open` up to 28 closed bars back");
    }

    [Fact]
    public void A_lagged_read_in_any_position_of_the_condition_is_found()
    {
        Warned("not rising(vwap, 25)", Timeframe.M15).Length.ShouldBe(1);
        Warned("rsi(14) > 55 and (ema(20) > ema(50) or between(close, vwap[25], 100))", Timeframe.M15).Length.ShouldBe(1);
        Warned("min(vwap[25], close) > abs(today_open[26])", Timeframe.M15).Length.ShouldBe(2);
        Warned("rising(vwap, 25) and falling(vwap, 26)", Timeframe.M15).Length.ShouldBe(2);
    }

    [Fact]
    public void A_lagged_call_that_could_not_be_bound_still_gets_it_so_that_fixing_the_other_fault_does_not_bring_it_in()
    {
        var result = Compiling.Of("rising(vwap + true, 25)");

        result.IsSuccess.ShouldBeFalse();
        result.Problems.Select(p => p.Code).ShouldBe([LintCodes.LagAcrossSession, LintCodes.TypeMismatch]);
        Compiling.Of("rising(vwap, 25)").Problems.Select(p => p.Code).ShouldBe([LintCodes.LagAcrossSession]);
    }

    [Fact]
    public void A_count_given_by_a_parameter_is_read_as_the_number_it_is()
    {
        var result = Compiling.Of("rising(vwap, n)", Timeframe.M5, Compiling.Params(("n", 75)));

        var warning = result.Problems.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LintCodes.LagAcrossSession);
        warning.Message.ShouldStartWith("`rising(vwap, n)` reads `vwap` up to 75 closed bars back, but a session holds at most 75 bars of 5m");
        Warned("rising(vwap, n)", Timeframe.M5, Compiling.Params(("n", 74))).ShouldBeEmpty();
    }

    [Fact]
    public void A_condition_that_reads_less_far_back_has_no_warning_and_the_same_plan_as_with_a_time_check()
    {
        var bare = Compiling.Condition("rising(vwap, 24)");
        var checkedOne = Compiling.Condition("rising(vwap, 24) and time >= \"09:45\"");

        bare.Warnings.ShouldBeEmpty();
        checkedOne.Warnings.ShouldBeEmpty();
        checkedOne.Features.ShouldBe(bare.Features);
        checkedOne.HistoryDepth.ShouldBe(bare.HistoryDepth);
    }
}
