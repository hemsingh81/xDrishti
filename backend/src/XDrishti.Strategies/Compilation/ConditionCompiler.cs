using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// Reads, checks and plans one condition: parse (<see cref="ConditionParser"/>), bind every name to the catalog and type every node (the binder), apply the look-ahead and timeframe rules (the lint), and
/// describe what the feature frame must provide (<see cref="CompiledCondition"/>, which is compiled to a delegate when it is bound to a frame). Pure and thread-safe: no clock, no I/O, no state between calls. A bad condition is never an exception: every problem found is returned,
/// in source order. Only a programming error (a null argument, a timeframe that is not one of the nine) throws.
/// </summary>
public static class ConditionCompiler
{
    /// <summary>Compiles the text of one condition for the context (the evaluation timeframe and the strategy parameters).</summary>
    public static CompileResult Compile(string source, CompileContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Parameters);
        if (!Enum.IsDefined(context.EvaluationTimeframe))
        {
            throw new ArgumentOutOfRangeException(nameof(context), context.EvaluationTimeframe, "The evaluation timeframe is not one of the nine timeframes.");
        }

        var parsed = ConditionParser.Parse(source);
        if (!parsed.IsSuccess)
        {
            return new CompileResult(null, parsed.Problems);
        }

        // The language is case-sensitive, so the names of the parameters are matched ordinally whatever comparer the caller's map has (a map from a config file is often case-insensitive); the copy also means that the
        // plan shares nothing with the caller's map.
        var parameters = new Dictionary<string, double>(context.Parameters, StringComparer.Ordinal);
        var scope = new BindingScope(source, context.EvaluationTimeframe, parameters);
        IBoundExpression tree;
        LintFacts facts;
        try
        {
            tree = new Binder(scope).BindCondition(parsed.Tree!);
            facts = Linter.Run(tree, scope);
        }
        catch (InsufficientExecutionStackException)
        {
            // The parser keeps the nesting far inside the stack; this is for a caller that is already deep in its own stack.
            var cannot = new Problem(SyntaxCodes.TooDeep, "The condition cannot be checked here: there is not enough stack left for its nesting.", new SourceSpan(0, 0, 1, 1));
            return new CompileResult(null, [cannot]);
        }

        var problems = scope.Problems.OrderBy(p => p.Span.Start).ToList().AsReadOnly();
        if (problems.Any(p => p.Severity == ProblemSeverity.Error))
        {
            return new CompileResult(null, problems);
        }

        var warnings = problems.Where(p => p.Severity == ProblemSeverity.Warning).ToList().AsReadOnly();
        var condition = new CompiledCondition(source, context.EvaluationTimeframe, facts.Features, facts.HistoryDepth, facts.NeedsContext, facts.NeedsExpiryDays, warnings, tree);
        return new CompileResult(condition, problems);
    }
}
