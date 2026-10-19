using System.Reflection;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>Every code of <c>ModelCodes</c> has a failing example (exact code, path and severity) and a near miss that compiles without that problem (spec P3-02b, <i>Acceptance criteria</i>, preamble).</summary>
public sealed class ModelCodeTests
{
    public static TheoryData<ModelCodeExample> Examples { get; } = [.. ModelCodeExample.All];

    [Fact]
    public void Every_code_of_the_model_has_exactly_one_failing_example_and_there_are_47_of_them()
    {
        var codes = typeof(ModelCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        codes.Count.ShouldBe(47);
        codes.ShouldAllBe(c => c.StartsWith("strategy.model.", StringComparison.Ordinal));
        codes.ShouldBeUnique();
        ModelCodeExample.All.Select(e => e.Code).Order().ShouldBe(codes.Order(), "a code without a failing example, or an example for a code that does not exist");
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void The_failing_example_gives_its_code_at_its_path_with_its_severity(ModelCodeExample example)
    {
        var result = Located.Compile(example.Bad, example.BadTimeframe, example.BadSide);

        var found = result.ShouldHave(example.Code, example.Path, example.Severity);
        found.Problem.Span.ShouldBe(new SourceSpan(0, 0, 1, 1), "a problem of the model has no text");
        result.IsSuccess.ShouldBe(example.Severity != ProblemSeverity.Error && !result.Errors.Any(), "the compile fails on errors only");
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void The_near_miss_compiles_without_that_problem(ModelCodeExample example)
    {
        var result = Located.Compile(example.Near, example.NearTimeframe ?? example.BadTimeframe, example.NearSide ?? example.BadSide);

        result.ShouldNotHave(example.Code, example.Path);
        if (example.Code == ModelCodes.TooManyProblems)
        {
            // the near miss of the cap has exactly as many errors as are listed, so none is dropped
            result.Problems.Count.ShouldBe(100, Located.Show(result));
            return;
        }

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
    }

    [Fact]
    public void The_codes_of_the_cap_are_an_error_when_errors_were_dropped_and_an_information_when_only_warnings_were()
    {
        var example = ModelCodeExample.All.Single(e => e.Code == ModelCodes.TooManyProblems);

        var result = Located.Compile(example.Bad);

        result.ShouldHave(ModelCodes.TooManyProblems, string.Empty, ProblemSeverity.Error);
    }

    [Fact]
    public void Compiling_a_timeframe_that_is_not_an_enum_member_or_a_null_definition_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => StrategyCompiler.Compile(null!, Timeframe.M15, XDrishti.Domain.Research.Side.Buy));
        Should.Throw<ArgumentOutOfRangeException>(() => StrategyCompiler.Compile(Defs.Basic(), (Timeframe)99, XDrishti.Domain.Research.Side.Buy));
        Should.Throw<ArgumentOutOfRangeException>(() => StrategyCompiler.Compile(Defs.Basic(), Timeframe.M15, (XDrishti.Domain.Research.Side)5));
    }
}
