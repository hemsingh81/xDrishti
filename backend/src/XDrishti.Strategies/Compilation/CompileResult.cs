namespace XDrishti.Strategies.Compilation;

/// <summary>
/// What <see cref="ConditionCompiler.Compile"/> returns: the <see cref="Condition"/> when the condition is valid, otherwise every problem found, in source order. The library's <c>Result</c> carries one error, not a list
/// of problems with positions, so a result type of its own is used (as <c>ParseResult</c> is for the parser). <see cref="Problems"/> holds all problems of the compile: errors when it failed, and the warnings and
/// the information (<c>needs_context</c>) when it succeeded.
/// </summary>
public sealed record CompileResult(CompiledCondition? Condition, IReadOnlyList<Problem> Problems)
{
    /// <summary>True when the condition is valid and <see cref="Condition"/> is set.</summary>
    public bool IsSuccess => Condition is not null;

    /// <summary>The problems that stopped the compile (empty on success).</summary>
    public IEnumerable<Problem> Errors => Problems.Where(p => p.Severity == ProblemSeverity.Error);
}
