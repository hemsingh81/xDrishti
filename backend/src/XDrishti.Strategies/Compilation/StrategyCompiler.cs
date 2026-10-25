using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// Checks a <see cref="StrategyDefinition"/> and compiles one side of it for one evaluation timeframe (spec P3-02b, ADR 0036): the definition (schema, id, style, timeframes, sides, at most four parameters, the filters), the
/// time rules, and for the side the setup, trigger and invalidate lists (every condition compiled as its own condition with the language of P3-02), the entry, the stop, the exits and the overlays. The result is a
/// <see cref="CompiledSide"/> or the located problems, every one with a path in the model (<c>buy.trigger[1]</c>) and, for a problem of the language, the span inside that item. Pure and thread-safe: no clock, no I/O, no state
/// between calls; the definition is read and never changed, and the side keeps no reference to its lists. A bad definition is never an exception; only a programming error (a null argument, a timeframe or side that is not one
/// of the enum's) throws.
/// <para>
/// How the sections combine: at every closed bar of the evaluation timeframe they are evaluated together, and an entry is possible when the setup is true, the trigger is true and the invalidate list is not true
/// (<see cref="SideEvaluation.Fires"/>).
/// </para>
/// </summary>
public static class StrategyCompiler
{
    /// <summary>Compiles the side of the definition for the evaluation timeframe.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="evaluationTimeframe"/> or <paramref name="side"/> is not a member of its enum.</exception>
    public static StrategyCompileResult Compile(StrategyDefinition definition, Timeframe evaluationTimeframe, Side side)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Enum.IsDefined(evaluationTimeframe))
        {
            throw new ArgumentOutOfRangeException(nameof(evaluationTimeframe), evaluationTimeframe, "The evaluation timeframe is not one of the nine timeframes.");
        }

        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "The side is not BUY or SELL.");
        }

        var sink = new ProblemSink();
        var checks = new ModelChecks(sink);
        var declaredSides = checks.CheckIdentity(definition, evaluationTimeframe, side);
        var declared = checks.CheckParams(definition);

        var rules = side == Side.Buy ? definition.Buy : definition.Sell;
        if (rules is null && side == Side.Sell && StrategyMirror.Applies(definition))
        {
            // a definition that reaches the compiler without its SELL (the Lab expands before; a caller that does not gets the same mirror here)
            var (expanded, mirrorProblems) = StrategyMirror.Expand(definition);
            rules = expanded.Sell;
            foreach (var located in mirrorProblems.Where(m => m.Problem.Severity == ProblemSeverity.Error))
            {
                sink.Condition(located.Path, located.Problem);
            }
        }

        SideCompiler.Parts? parts = null;
        if (rules is not null)
        {
            parts = new SideCompiler(sink, declared).Compile(rules, side, evaluationTimeframe, definition.Style, definition.TimeRules?.MaxHoldSessions);
        }
        else if (declaredSides.Contains(side) && !(side == Side.Sell && StrategyMirror.Applies(definition)))
        {
            if (side == Side.Buy)
            {
                sink.Error("buy", ModelCodes.SideRulesMissing, "The strategy lists BUY but has no `buy` rules: write the setup, trigger, entry, stop and exits.");
            }
            else
            {
                sink.Error("sell", ModelCodes.SellBlockMissing, "The strategy lists SELL but has no `sell` block: write it. There is no BUY block to mirror.");
            }
        }

        var sessionRules = new TimeRulesCheck(sink).Check(definition, evaluationTimeframe);
        var filters = checks.CheckFilters(definition);

        var problems = sink.Build();
        if (sink.HasErrors || parts is null)
        {
            return new StrategyCompileResult(null, problems);
        }

        var warnings = problems.Where(p => p.Problem.Severity == ProblemSeverity.Warning).ToList().AsReadOnly();
        var compiled = new CompiledSide(
            definition.Id,
            side,
            definition.Style,
            evaluationTimeframe,
            parts.Setup,
            parts.Trigger!,
            parts.Invalidate,
            parts.Entry,
            parts.Stop!,
            parts.Exits,
            parts.Overlays,
            sessionRules,
            filters,
            parts.Features,
            parts.HistoryDepth,
            parts.NeedsContext,
            parts.NeedsExpiryDays,
            warnings);
        return new StrategyCompileResult(compiled, problems);
    }
}
