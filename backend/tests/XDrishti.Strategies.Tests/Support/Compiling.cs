using System.Collections.Frozen;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Short ways to compile a condition in a test and to look at what came out. The default strategy timeframe is 15m, the one most of the spec's examples use.</summary>
internal static class Compiling
{
    public static readonly IReadOnlyDictionary<string, double> NoParameters = FrozenDictionary<string, double>.Empty;

    public static CompileResult Of(string source, Timeframe timeframe = Timeframe.M15, IReadOnlyDictionary<string, double>? parameters = null) =>
        ConditionCompiler.Compile(source, new CompileContext(timeframe, parameters ?? NoParameters));

    /// <summary>The condition that must compile without errors.</summary>
    public static CompiledCondition Condition(string source, Timeframe timeframe = Timeframe.M15, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var result = Of(source, timeframe, parameters);
        result.IsSuccess.ShouldBeTrue(source + " -> " + Describe(result));
        return result.Condition!;
    }

    /// <summary>The problems of a condition that must fail to compile.</summary>
    public static IReadOnlyList<Problem> Failures(string source, Timeframe timeframe = Timeframe.M15, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var result = Of(source, timeframe, parameters);
        result.IsSuccess.ShouldBeFalse(source);
        result.Condition.ShouldBeNull();
        return result.Problems;
    }

    /// <summary>The single error of a condition that must fail with exactly one error (warnings are not counted).</summary>
    public static Problem OnlyError(string source, Timeframe timeframe = Timeframe.M15, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var errors = Failures(source, timeframe, parameters).Where(p => p.Severity == ProblemSeverity.Error).ToList();
        errors.Count.ShouldBe(1, source + " -> " + string.Join(" | ", errors.Select(e => e.Code + ": " + e.Message)));
        return errors[0];
    }

    public static IReadOnlyDictionary<string, double> Params(params (string Name, double Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    public static string Describe(CompileResult result) => string.Join(" | ", result.Problems.Select(p => $"{p.Severity} {p.Code} [{p.Span.Start},{p.Span.End}) {p.Message}"));
}
