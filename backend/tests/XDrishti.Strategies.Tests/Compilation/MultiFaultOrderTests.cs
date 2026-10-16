using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>Criterion 6, last part: all the problems of a source with several faults are reported together, in source order, whichever rule finds them.</summary>
public sealed class MultiFaultOrderTests
{
    [Fact]
    public void A_source_with_a_fault_of_every_kind_reports_them_all_in_the_order_they_are_written()
    {
        const string Source = "closee > 1 and rsi(0) > 5 and close[0] > 1 and crosses_above(close, tf(\"1D\").ema(20)) and time >= \"9:45\" and rising(close, 300) and tf(\"2h\").close > rsi(14, 3) and close > true";

        var result = Compiling.Of(Source);

        ProblemAssert.Match(
            Source,
            result.Problems,
            new Expected(LintCodes.UnknownName, "closee", "close"),
            new Expected(LintCodes.BadArguments, "0"),
            new Expected(LintCodes.NegativeOrZeroLag, "0", "close", Nth: 1),
            new Expected(LintCodes.MixedTimeframesInLag, "crosses_above(close, tf(\"1D\").ema(20))", "close > tf(\"1D\").ema(20) and close[1] <= tf(\"1D\").ema(20)"),
            new Expected(LintCodes.BadTimeLiteral, "\"9:45\"", "\"09:45\""),
            new Expected(LintCodes.LagTooDeep, "rising(close, 300)"),
            new Expected(LintCodes.UnsupportedTimeframe, "\"2h\"", "\"1h\""),
            new Expected(LintCodes.BadArguments, "3", Nth: 1),
            new Expected(LintCodes.TypeMismatch, "close > true"));
        result.Problems.Select(p => p.Span.Start).ShouldBeInOrder();
    }

    [Fact]
    public void The_same_faults_on_several_lines_have_their_own_lines_and_columns()
    {
        const string Source = "closee > 1\n  and rsi(0) > 5\n  and time >= \"9:45\"";

        var result = Compiling.Of(Source);

        result.Problems.Select(p => (p.Code, p.Span.Line, p.Span.Column)).ShouldBe([(LintCodes.UnknownName, 1, 1), (LintCodes.BadArguments, 2, 11), (LintCodes.BadTimeLiteral, 3, 15)]);
    }

    [Fact]
    public void Warnings_come_in_the_same_order_as_the_errors_around_them()
    {
        const string Source = "1 < 2 and closee > 1 and tf(\"15m\").ema(20) > 0";

        var result = Compiling.Of(Source);

        result.Problems.Select(p => (p.Code, p.Severity)).ShouldBe(
            [(LintCodes.ConstantCondition, ProblemSeverity.Warning), (LintCodes.UnknownName, ProblemSeverity.Error), (LintCodes.RedundantTimeframe, ProblemSeverity.Warning)]);
        result.Condition.ShouldBeNull();
    }

    /// <summary>The failing examples of the table that are one condition each on the 15m strategy and read no parameter that another one defines.</summary>
    private static readonly string[] Snippets =
    [
        "unknown_name: a typo of a feature", "unknown_name: a typo of a function", "unknown_name: the wrong case", "unknown_name: a name with no neighbour", "unknown_name: a typo after a scope",
        "bad_arguments: below the range", "bad_arguments: above the range", "bad_arguments: not a whole number", "bad_arguments: one argument too many", "bad_arguments: fast not below slow",
        "bad_arguments: an argument that changes with the data", "bad_arguments: a source that is not a price", "bad_arguments: a function written without parentheses", "bad_arguments: too few arguments for a function",
        "unsupported_timeframe: 1d in lower case", "unsupported_timeframe: a timeframe near a real one", "unsupported_timeframe: vwap on 1D by tf", "unsupported_timeframe: a date feature on 1h",
        "negative_or_zero_lag: a subscript of zero", "negative_or_zero_lag: a negative subscript", "negative_or_zero_lag: a rising count of zero", "negative_or_zero_lag: a rising count that is not whole",
        "lag_too_deep: a subscript of 256", "lag_too_deep: a rising count of 256", "lag_too_deep: lags that add up inside rising", "lag_too_deep: the extra lag of a crossing",
        "lag_on_non_feature: a constant", "lag_on_non_feature: the time",
        "mixed_timeframes_in_lag: a crossing of the close and a daily average", "mixed_timeframes_in_lag: a sum of two timeframes in rising",
        "bad_time_literal: one digit for the hour", "bad_time_literal: before the open", "bad_time_literal: an hour that does not exist",
        "type_mismatch: the time against a number", "type_mismatch: a number against a true/false value", "type_mismatch: ordering a regime", "type_mismatch: not of a number", "type_mismatch: two regimes",
    ];

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(0, 60)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Joining_failing_conditions_reports_the_problems_of_each_in_order_shifted_to_where_it_stands(int seed)
    {
        var random = new Random(seed);
        var chosen = Enumerable.Range(0, random.Next(2, 5)).Select(_ => Snippets[random.Next(Snippets.Length)]).ToList();
        var parts = chosen.Select(label => LintCases.Failing[label]).ToList();
        parts.ShouldAllBe(p => p.Evaluation == Timeframe.M15 && p.Parameters.Count == 0);

        // no parentheses: a parenthesised part has the brackets in its span, which would move the span of a problem about the whole part
        var source = string.Join(" and ", parts.Select(p => p.Source));
        var combined = ConditionCompiler.Compile(source, new CompileContext(Timeframe.M15)).Problems;

        var expected = new List<(string Code, int Start, int End, string? Suggestion, string Message)>();
        var offset = 0;
        foreach (var part in parts)
        {
            var alone = ConditionCompiler.Compile(part.Source, new CompileContext(Timeframe.M15)).Problems;
            expected.AddRange(alone.Select(p => (p.Code, p.Span.Start + offset, p.Span.End + offset, p.Suggestion, p.Message)));
            offset += part.Source.Length + " and ".Length;
        }

        combined.Select(p => (p.Code, p.Span.Start, p.Span.End, p.Suggestion, p.Message)).ShouldBe(expected, source);
        combined.Select(p => p.Span.Start).ShouldBeInOrder();
        foreach (var problem in combined)
        {
            (problem.Span.Line, problem.Span.Column).ShouldBe(SourceLocator.At(source, problem.Span.Start));
        }
    }
}
