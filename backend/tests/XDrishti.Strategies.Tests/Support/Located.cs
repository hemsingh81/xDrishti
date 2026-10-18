using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Short ways to compile a strategy in a test and to look at the located problems that come out.</summary>
internal static class Located
{
    public static StrategyCompileResult Compile(StrategyDefinition definition, Timeframe timeframe = Timeframe.M15, Side side = Side.Buy) => StrategyCompiler.Compile(definition, timeframe, side);

    /// <summary>The side of a definition that must compile; the failure message lists the problems.</summary>
    public static CompiledSide Ok(StrategyDefinition definition, Timeframe timeframe = Timeframe.M15, Side side = Side.Buy)
    {
        var result = Compile(definition, timeframe, side);
        result.IsSuccess.ShouldBeTrue(Show(result));
        return result.Compiled!;
    }

    public static string Show(StrategyCompileResult result) =>
        string.Join("\n", result.Problems.Select(p => $"  {p.Problem.Severity} {p.Problem.Code} at '{p.Path}' [{p.Problem.Span.Start},{p.Problem.Span.End}) {p.Problem.Message}"));

    /// <summary>The problems with the code at the path.</summary>
    public static List<LocatedProblem> At(this StrategyCompileResult result, string code, string path) => [.. result.Problems.Where(p => p.Problem.Code == code && p.Path == path)];

    /// <summary>Fails unless the result has exactly one problem with this code at this path, of this severity; returns it.</summary>
    public static LocatedProblem ShouldHave(this StrategyCompileResult result, string code, string path, ProblemSeverity severity = ProblemSeverity.Error)
    {
        var found = result.At(code, path);
        found.Count.ShouldBe(1, $"expected {code} at '{path}', the problems are:\n{Show(result)}");
        found[0].Problem.Severity.ShouldBe(severity, $"{code} at '{path}'");
        return found[0];
    }

    public static void ShouldNotHave(this StrategyCompileResult result, string code, string path) =>
        result.At(code, path).ShouldBeEmpty($"{code} at '{path}' was not expected; the problems are:\n{Show(result)}");

    /// <summary>The errors of a result as <c>code@path</c>, in order.</summary>
    public static string[] ErrorList(this StrategyCompileResult result) => [.. result.Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error).Select(p => p.Problem.Code.Replace("strategy.", string.Empty, StringComparison.Ordinal) + "@" + p.Path)];
}
