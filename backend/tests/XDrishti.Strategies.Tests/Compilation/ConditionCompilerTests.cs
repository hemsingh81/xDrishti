using System.Collections.Frozen;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation;

/// <summary>The compiler's contract: what it returns for good and bad text, what is a programming error, that it is deterministic and thread-safe, and that limits give problems and not crashes.</summary>
public sealed class ConditionCompilerTests
{
    [Fact]
    public void A_null_text_or_context_or_parameter_map_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => ConditionCompiler.Compile(null!, new CompileContext(Timeframe.M15)));
        Should.Throw<ArgumentNullException>(() => ConditionCompiler.Compile("close > 1", null!));
        Should.Throw<ArgumentNullException>(() => ConditionCompiler.Compile("close > 1", new CompileContext(Timeframe.M15, null!)));
    }

    [Fact]
    public void A_timeframe_that_is_none_of_the_nine_is_a_programming_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ConditionCompiler.Compile("close > 1", new CompileContext((Timeframe)99)));
        Should.Throw<ArgumentOutOfRangeException>(() => ConditionCompiler.Compile("close > 1", new CompileContext((Timeframe)(-1))));
    }

    [Fact]
    public void A_context_without_parameters_has_an_empty_map()
    {
        var context = new CompileContext(Timeframe.H1);

        context.EvaluationTimeframe.ShouldBe(Timeframe.H1);
        context.Parameters.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", SyntaxCodes.Empty)]
    [InlineData("   ", SyntaxCodes.Empty)]
    [InlineData("rsi(14) >", SyntaxCodes.UnexpectedEnd)]
    [InlineData("rsi(14", SyntaxCodes.UnclosedBracket)]
    [InlineData("a < b < c", SyntaxCodes.ChainedComparison)]
    [InlineData("rsi(14)[1] > 0", SyntaxCodes.SubscriptOnNonReference)]
    [InlineData("close > 1 $", SyntaxCodes.UnknownCharacter)]
    public void A_text_that_does_not_parse_returns_the_syntax_problems_and_nothing_from_the_binder(string source, string code)
    {
        var result = Compiling.Of(source);

        result.IsSuccess.ShouldBeFalse();
        result.Condition.ShouldBeNull();
        result.Problems.ShouldBe(ConditionParser.Parse(source).Problems);
        result.Problems[0].Code.ShouldBe(code);
    }

    [Fact]
    public void A_text_that_is_too_long_or_too_deep_is_a_problem()
    {
        var tooLong = Compiling.Of("close > 1 " + new string(' ', 4000));
        var tooDeep = Compiling.Of(new string('(', 65) + "close > 1" + new string(')', 65));

        tooLong.Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.TooLong);
        tooDeep.Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.TooDeep);
    }

    [Fact]
    public void The_deepest_accepted_conditions_compile()
    {
        var parentheses = new string('(', 63) + "rsi(14) > 55" + new string(')', 63);
        var chain = string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", 63));
        var calls = string.Concat(Enumerable.Repeat("abs(", 31)) + "close" + new string(')', 31) + " > 1";

        Compiling.Condition(parentheses).Features.Count.ShouldBe(1);
        Compiling.Condition(chain).Features.Count.ShouldBe(1);
        Compiling.Condition(calls).Features.Count.ShouldBe(1);
        Compiling.Of(string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", 64))).Problems.ShouldHaveSingleItem().Code.ShouldBe(SyntaxCodes.TooDeep);
    }

    [Fact]
    public void A_long_condition_of_many_different_features_compiles_in_one_pass()
    {
        var parts = Enumerable.Range(1, 60).Select(n => $"ema({n}) > sma({n + 1})");
        var source = string.Join(" or ", parts);

        var plan = Compiling.Condition(source);

        plan.Features.Count.ShouldBe(120);
        plan.HistoryDepth.ShouldBe(1);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    public void The_deepest_accepted_conditions_compile_on_a_thread_with_a_small_stack(int kilobytes)
    {
        var source = string.Concat(Enumerable.Repeat("abs(", 31)) + "close" + new string(')', 31) + " > 1 and " + string.Join(" and ", Enumerable.Repeat("rsi(14) > 55", 30));
        CompileResult? result = null;

        var thread = new Thread(() => result = Compiling.Of(source), kilobytes * 1024);
        thread.Start();
        thread.Join();

        // a thread with little stack gets a problem about the nesting, never a crash
        result.ShouldNotBeNull();
        (result.IsSuccess || result.Problems.Any(p => p.Code == SyntaxCodes.TooDeep)).ShouldBeTrue(Compiling.Describe(result));
        if (kilobytes >= 1024)
        {
            result.IsSuccess.ShouldBeTrue(Compiling.Describe(result));
        }
    }

    [Fact]
    public void Compiling_the_same_text_twice_gives_the_same_result()
    {
        var context = new CompileContext(Timeframe.M15, FrozenDictionary<string, double>.Empty);
        string[] sources = ["rsi(14) > 55 and crosses_above(close, vwap)", "closee > 1 and rsi(0) > 5", "1 < 2", "regime() == \"UP_LOW_VOL\" and is_expiry_day == 1"];

        foreach (var source in sources)
        {
            var first = ConditionCompiler.Compile(source, context);
            var second = ConditionCompiler.Compile(source, context);

            second.Problems.ShouldBe(first.Problems, source);
            second.IsSuccess.ShouldBe(first.IsSuccess);
            second.Condition?.Features.ShouldBe(first.Condition!.Features);
            second.Condition?.HistoryDepth.ShouldBe(first.Condition!.HistoryDepth);
            second.Condition?.Warnings.ShouldBe(first.Condition!.Warnings);
        }
    }

    [Fact]
    public void Many_threads_compiling_at_once_get_the_results_one_thread_gets()
    {
        string[] sources = ["rsi(14) > 55 and crosses_above(close, vwap)", "closee > 1 and rsi(0) > 5", "1 < 2", "regime() == \"UP_LOW_VOL\" and is_expiry_day == 1", "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"];
        var expected = sources.Select(s => Compiling.Of(s)).ToList();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 200; i++)
            {
                var index = i % sources.Length;
                var actual = Compiling.Of(sources[index]);
                if (!actual.Problems.SequenceEqual(expected[index].Problems) || actual.IsSuccess != expected[index].IsSuccess
                    || (actual.IsSuccess && !actual.Condition!.Features.SequenceEqual(expected[index].Condition!.Features)))
                {
                    failures.Add(sources[index]);
                }
            }
        });

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void A_plan_has_the_text_and_the_timeframe_it_was_made_for_and_its_lists_cannot_be_changed_by_the_caller()
    {
        var plan = Compiling.Condition("1 < 2 and rsi(14) > 55", Timeframe.H1);

        plan.Source.ShouldBe("1 < 2 and rsi(14) > 55");
        plan.EvaluationTimeframe.ShouldBe(Timeframe.H1);
        plan.Features.ShouldBeAssignableTo<IReadOnlyList<XDrishti.Indicators.FeatureSpec>>();
        (plan.Features is IList<XDrishti.Indicators.FeatureSpec> list && !list.IsReadOnly).ShouldBeFalse();
        (plan.Warnings is IList<Problem> warnings && !warnings.IsReadOnly).ShouldBeFalse();
    }

    [Fact]
    public void The_text_of_a_failing_compile_has_no_plan_and_a_successful_one_has_no_errors()
    {
        var bad = Compiling.Of("closee > 1");
        var good = Compiling.Of("close > 1");

        bad.Condition.ShouldBeNull();
        bad.Errors.ShouldNotBeEmpty();
        good.Condition.ShouldNotBeNull();
        good.Errors.ShouldBeEmpty();
        good.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Every_problem_of_every_failing_example_has_a_line_and_a_column_that_match_its_offset()
    {
        foreach (var (label, lintCase) in Lint.LintCases.Failing)
        {
            var result = ConditionCompiler.Compile(lintCase.Source, new CompileContext(lintCase.Evaluation, lintCase.Parameters));

            foreach (var problem in result.Problems)
            {
                problem.Message.ShouldNotBeNullOrWhiteSpace(label);
                problem.Span.Line.ShouldBeGreaterThanOrEqualTo(1);
                problem.Span.Column.ShouldBeGreaterThanOrEqualTo(1);
                (problem.Span.Line, problem.Span.Column).ShouldBe(SourceLocator.At(lintCase.Source, problem.Span.Start), label);
                problem.Span.End.ShouldBeGreaterThanOrEqualTo(problem.Span.Start);
                problem.Span.End.ShouldBeLessThanOrEqualTo(lintCase.Source.Length);
                problem.Code.ShouldStartWith("strategy.", Case.Sensitive);
            }
        }
    }
}
