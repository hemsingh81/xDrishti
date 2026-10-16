using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Lint;

/// <summary>
/// The rules around lags (<c>lag_on_non_feature</c>, <c>mixed_timeframes_in_lag</c>, <c>lag_too_deep</c>) on every pair of the timeframes 1m, 5m, 15m, 1h, 1D and 1W: the strategy runs on the first of the pair and a feature
/// of the second is mixed in. A lagged call whose features sit on two timeframes is refused; lagging each feature by hand is allowed on any pair; the depth rule does not depend on the timeframes at all.
/// </summary>
public sealed class LagRuleMatrixTests
{
    private static readonly (Timeframe Frame, string Code)[] Frames =
    [
        (Timeframe.M1, "1m"), (Timeframe.M5, "5m"), (Timeframe.M15, "15m"), (Timeframe.H1, "1h"), (Timeframe.D1, "1D"), (Timeframe.W1, "1W"),
    ];

    public static TheoryData<Timeframe, Timeframe> Pairs { get; } = [.. Frames.SelectMany(a => Frames.Select(b => (a.Frame, b.Frame)))];

    public static TheoryData<Timeframe> Strategies { get; } = [.. Frames.Select(f => f.Frame)];

    private static string Code(Timeframe frame) => Frames.Single(f => f.Frame == frame).Code;

    private static IReadOnlyList<Problem> Errors(string source, Timeframe strategy, IReadOnlyDictionary<string, double>? parameters = null) =>
        [.. Compiling.Of(source, strategy, parameters).Errors];

    [Fact]
    public void The_matrix_covers_thirty_six_ordered_pairs()
    {
        Frames.Length.ShouldBe(6);
        Pairs.Count.ShouldBe(36);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_crossing_is_refused_when_its_two_sides_are_on_two_timeframes_and_allowed_on_one(Timeframe strategy, Timeframe other)
    {
        var o = Code(other);

        var upward = Errors($"crosses_above(close, tf(\"{o}\").close)", strategy);
        var downward = Errors($"crosses_below(tf(\"{Code(strategy)}\").close, tf(\"{o}\").ema(20))", strategy);

        if (strategy == other)
        {
            upward.ShouldBeEmpty();
            downward.ShouldBeEmpty();
        }
        else
        {
            upward.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.MixedTimeframesInLag);
            downward.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.MixedTimeframesInLag);
            upward[0].Message.ShouldContain(Code(strategy) + " and " + o, Case.Sensitive, "names the two timeframes");
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Rising_and_falling_of_an_expression_are_refused_on_two_timeframes_but_two_calls_on_two_timeframes_are_fine(Timeframe strategy, Timeframe other)
    {
        var o = Code(other);

        var mixed = Errors($"rising(close + tf(\"{o}\").close, 2)", strategy);
        var mixedFalling = Errors($"falling(close * tf(\"{o}\").volume, 1)", strategy);
        var separate = Errors($"falling(tf(\"{o}\").close, 3) and rising(close, 3)", strategy);

        separate.ShouldBeEmpty("each call has its own timeframe");
        if (strategy == other)
        {
            mixed.ShouldBeEmpty();
            mixedFalling.ShouldBeEmpty();
        }
        else
        {
            mixed.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.MixedTimeframesInLag);
            mixedFalling.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.MixedTimeframesInLag);
        }
    }

    /// <summary>
    /// The calendar and context features change once a date, so their previous value is the previous <i>date</i>; every bar feature, on 1m too, has the previous <i>bar</i>. A lagged call that reads both would compare
    /// two different moments, so a feature of the date is a clock of its own and is refused next to a bar feature on every strategy timeframe, written plainly or with <c>tf("1m")</c> (the one timeframe the date
    /// features can be written with).
    /// </summary>
    [Theory]
    [MemberData(nameof(Strategies))]
    public void A_feature_of_the_date_is_a_clock_of_its_own_and_is_refused_next_to_a_bar_feature_on_every_strategy_timeframe(Timeframe strategy)
    {
        string[] mixed =
        [
            "crosses_above(close, vix)", "crosses_below(vix, ema(20))", "crosses_above(close, index.trend)", "crosses_below(close, breadth)", "crosses_above(close, day_of_week)",
            "rising(close * vix, 2)", "falling(volume - day_of_week, 1)", "rising(close + regime_vol, 3)", "crosses_above(tf(\"1m\").close, vix)", "crosses_below(vix, tf(\"1m\").close)",
            "crosses_above(tf(\"1D\").close, vix_pct)", "rising(abs(close - vix), 2)", "falling(min(close, vix), 2)",
        ];

        foreach (var source in mixed)
        {
            var errors = Errors(source, strategy);

            var error = errors.ShouldHaveSingleItem(source + " on " + Code(strategy));
            error.Code.ShouldBe(LintCodes.MixedTimeframesInLag, source);
            error.Message.ShouldContain("the date (calendar and context features)", Case.Sensitive, source);
        }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void The_features_of_the_date_among_themselves_and_against_constants_are_allowed_in_a_lagged_call_on_every_strategy_timeframe(Timeframe strategy)
    {
        string[] allowed =
        [
            "crosses_above(vix, 20)", "crosses_below(vix, breadth)", "crosses_above(day_of_week, 3)", "rising(vix, 2)", "falling(vix - index_trend, 2)", "rising(vix[1] + vix, 2)", "crosses_above(vix, vix_pct)",
            "regime() == 3", "rising(close, 3) and rising(vix, 3)", "crosses_above(close, ema(20)) and crosses_above(vix, 20)",
        ];

        foreach (var source in allowed)
        {
            Errors(source, strategy).ShouldBeEmpty(source + " on " + Code(strategy));
        }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void The_explicit_form_the_message_offers_for_a_bar_feature_against_the_date_compiles_and_reads_the_previous_bar_only_for_the_bar_feature(Timeframe strategy)
    {
        var refused = Compiling.Of("crosses_above(close, vix)", strategy);

        var offered = refused.Errors.ShouldHaveSingleItem().Suggestion.ShouldNotBeNull();
        var plan = Compiling.Condition(offered, strategy);

        offered.ShouldBe("close > vix and close[1] <= vix");
        plan.HistoryDepth.ShouldBe(2, "only close is read one bar back; vix is read as it is now");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Lagging_each_feature_by_hand_is_allowed_on_any_pair_and_needs_the_deeper_history(Timeframe strategy, Timeframe other)
    {
        var o = Code(other);

        var plan = Compiling.Condition($"close > tf(\"{o}\").close and close[1] <= tf(\"{o}\").close and tf(\"{o}\").close[2] > close[3]", strategy);

        plan.HistoryDepth.ShouldBe(4, "the largest lag is 3");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_subscript_on_something_that_is_not_a_feature_is_refused_on_any_pair(Timeframe strategy, Timeframe other)
    {
        var errors = Errors($"lagp[1] > tf(\"{Code(other)}\").close", strategy, Compiling.Params(("lagp", 5)));

        errors.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.LagOnNonFeature);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void The_depth_rule_does_not_depend_on_the_timeframes(Timeframe strategy, Timeframe other)
    {
        var o = Code(other);

        Compiling.Condition($"close[255] > tf(\"{o}\").close[255]", strategy).HistoryDepth.ShouldBe(256);
        Compiling.Condition($"rising(tf(\"{o}\").close[200], 55)", strategy).HistoryDepth.ShouldBe(256);
        Errors($"close[256] > tf(\"{o}\").close[255]", strategy).ShouldHaveSingleItem().Code.ShouldBe(LintCodes.LagTooDeep);
        Errors($"close[255] > tf(\"{o}\").close[256]", strategy).ShouldHaveSingleItem().Code.ShouldBe(LintCodes.LagTooDeep);
        Errors($"rising(tf(\"{o}\").close[200], 56)", strategy).ShouldHaveSingleItem().Code.ShouldBe(LintCodes.LagTooDeep);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void All_three_rules_report_together_in_source_order(Timeframe strategy, Timeframe other)
    {
        var o = Code(other);
        var source = $"crosses_above(close[255], tf(\"{o}\").close) and lagp[1] > 0 and rising(close[300], 2)";

        var codes = Errors(source, strategy, Compiling.Params(("lagp", 5))).Select(e => e.Code).ToList();

        var expected = new List<string>();
        if (strategy != other)
        {
            expected.Add(LintCodes.MixedTimeframesInLag);
        }

        expected.Add(LintCodes.LagTooDeep);
        expected.Add(LintCodes.LagOnNonFeature);
        expected.Add(LintCodes.LagTooDeep);
        codes.ShouldBe(expected);
    }

    /// <summary>
    /// A fault in one part of a lagged call (a count that is not a number of bars, a type error in an operand) does not hide the rule about the clocks of its operands: both are reported together, in source order, so
    /// that fixing one does not make the other appear.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_fault_in_one_part_of_a_lagged_call_does_not_hide_the_clock_rule_on_any_pair(Timeframe strategy, Timeframe other)
    {
        var o = Code(other);
        string[] mixed = strategy == other ? [] : [LintCodes.MixedTimeframesInLag];

        var badCount = Errors($"rising(close + tf(\"{o}\").close, 0.5)", strategy).Select(e => e.Code);
        var badOperandOfCrossing = Errors($"crosses_above(close + true, tf(\"{o}\").close)", strategy).Select(e => e.Code);
        var badOperandOfRun = Errors($"falling(close + true + tf(\"{o}\").close, 0)", strategy).Select(e => e.Code);
        var tooDeep = Errors($"crosses_below(close[255] + true, tf(\"{o}\").close)", strategy).Select(e => e.Code);

        badCount.ShouldBe([.. mixed, LintCodes.NegativeOrZeroLag]);
        badOperandOfCrossing.ShouldBe([.. mixed, LintCodes.TypeMismatch]);
        badOperandOfRun.ShouldBe([.. mixed, LintCodes.TypeMismatch, LintCodes.NegativeOrZeroLag]);
        tooDeep.ShouldBe([.. mixed, LintCodes.LagTooDeep, LintCodes.TypeMismatch], "the clocks and the depth are both reported for the call, then the type error");
    }
}
