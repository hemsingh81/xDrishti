namespace XDrishti.Strategies.Compilation;

/// <summary>
/// What <see cref="StrategyCompiler.Compile"/> returns: the <see cref="Compiled"/> side when there is no error, otherwise nothing but the located problems. <see cref="Problems"/> holds all of them, errors first (in the order
/// of the model, the first 100), then the warnings and the information (at most 100 in all); a <c>strategy.model.too_many_problems</c> stands in place of what was dropped. The compile never throws for a bad definition.
/// </summary>
public sealed record StrategyCompileResult(CompiledSide? Compiled, IReadOnlyList<LocatedProblem> Problems)
{
    /// <summary>True when the side compiled and <see cref="Compiled"/> is set.</summary>
    public bool IsSuccess => Compiled is not null;

    /// <summary>The problems that stopped the compile (empty on success).</summary>
    public IEnumerable<LocatedProblem> Errors => Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error);
}
