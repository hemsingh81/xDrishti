using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>The builtin functions: <c>crosses_above</c>, <c>crosses_below</c>, <c>rising</c>, <c>falling</c>, <c>between</c>, <c>abs</c>, <c>min</c>, <c>max</c> and <c>regime()</c>.</summary>
internal sealed partial class Binder
{
    /// <summary>The largest count a <c>rising</c> or <c>falling</c> node keeps; a larger one is refused by the lint, so the exact value does not matter.</summary>
    private const int MaxStoredCount = 1_000_000;

    private IBoundExpression BindBuiltin(Builtin builtin, Call call)
    {
        var name = Vocabulary.NameOf(builtin);
        var parameters = Vocabulary.ParameterNames(builtin);
        var ok = true;
        var arguments = new List<(IBoundExpression Bound, SourceSpan Span)>();
        foreach (var argument in call.Arguments)
        {
            if (argument is PositionalArgument positional)
            {
                arguments.Add((Bind(positional.Value), positional.Span));
            }
            else
            {
                scope.Error(LintCodes.BadArguments, "`" + name + "` takes no named arguments (" + BuiltinExample(builtin) + ").", argument.Span);
                ok = false;
            }
        }

        if (!ok)
        {
            // a named argument is already reported; the count of the positional ones says nothing about what was meant
            return new BoundInvalid(call.Span, [.. arguments.Select(a => a.Bound)]);
        }

        if (arguments.Count != parameters.Count)
        {
            var span = arguments.Count > parameters.Count ? arguments[parameters.Count].Span.To(arguments[^1].Span) : call.Span;
            scope.Error(
                LintCodes.BadArguments,
                "`" + BuiltinExample(builtin) + "` takes " + (parameters.Count == 0 ? "no arguments" : Count(parameters.Count, "argument")) + ", but " + Given(arguments.Count) + ".",
                span);
            return new BoundInvalid(call.Span, [.. arguments.Select(a => a.Bound)]);
        }

        if (builtin == Builtin.Regime)
        {
            IndicatorCatalog.TryGet("regime", out var regime);
            return BindFeature(regime, call.Name, [], null, call.Span, true);
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            // the count of rising/falling is checked as a constant below, not as a number of the data
            if (builtin is Builtin.Rising or Builtin.Falling && i == 1)
            {
                continue;
            }

            ok &= Expect(arguments[i].Bound, BoundType.Number, "Argument " + (i + 1) + " (`" + parameters[i] + "`) of `" + name + "` needs a number, but ");
        }

        if (builtin is Builtin.Rising or Builtin.Falling)
        {
            return BindRun(builtin, call, arguments[0].Bound, arguments[1], ok);
        }

        if (!ok)
        {
            // a crossing keeps its two operands as lagged ones, so that their clocks are still checked
            return new BoundInvalid(call.Span, [.. arguments.Select(a => a.Bound)], builtin is Builtin.CrossesAbove or Builtin.CrossesBelow ? new LaggedCall(2, 1) : null);
        }

        var bound = arguments.Select(a => a.Bound).ToList();
        return builtin switch
        {
            Builtin.CrossesAbove => new BoundCrossing(true, bound[0], bound[1], call.Span),
            Builtin.CrossesBelow => new BoundCrossing(false, bound[0], bound[1], call.Span),
            Builtin.Between => new BoundBetween(bound[0], bound[1], bound[2], call.Span),
            Builtin.Abs => new BoundAbs(bound[0], call.Span),
            Builtin.Min => new BoundMinMax(false, bound[0], bound[1], call.Span),
            _ => new BoundMinMax(true, bound[0], bound[1], call.Span),
        };
    }

    /// <summary><c>rising(x, n)</c> and <c>falling(x, n)</c>: the count is a whole number of at least 1 known when the condition is compiled.</summary>
    private IBoundExpression BindRun(Builtin builtin, Call call, IBoundExpression operand, (IBoundExpression Bound, SourceSpan Span) count, bool operandOk)
    {
        var name = Vocabulary.NameOf(builtin);
        if (count.Bound.Type == BoundType.Invalid)
        {
            return InvalidRun(call, operand, count.Bound, 0);
        }

        if (count.Bound.Type != BoundType.Number)
        {
            scope.Error(
                LintCodes.BadArguments,
                "The count of `" + name + "` must be a number, but " + scope.Quoted(count.Span) + " is " + Describe.Type(count.Bound.Type) + ".",
                count.Span);
            return InvalidRun(call, operand, count.Bound, 0);
        }

        if (!ConstantFolder.TryFold(count.Bound, out var steps))
        {
            scope.Error(
                LintCodes.BadArguments,
                "The count of `" + name + "` must be a number known when the condition is compiled (a number or a strategy parameter), but " + scope.Quoted(count.Span) + " changes from bar to bar.",
                count.Span);
            return InvalidRun(call, operand, count.Bound, 0);
        }

        if (!double.IsFinite(steps))
        {
            scope.Error(LintCodes.BadArguments, "The count of `" + name + "` must be a finite number, but " + scope.Quoted(count.Span) + " is " + Describe.Number(steps) + ".", count.Span);
            return InvalidRun(call, operand, count.Bound, 0);
        }

        if (steps < 1 || steps != Math.Floor(steps))
        {
            scope.Error(
                LintCodes.NegativeOrZeroLag,
                "The count of `" + name + "` is the number of closed bars to look back and must be a whole number from 1 up, but it is " + Describe.Number(steps) + ".",
                count.Span);
            return InvalidRun(call, operand, count.Bound, 0);
        }

        var stored = (int)Math.Min(steps, MaxStoredCount);
        return operandOk ? new BoundRun(builtin == Builtin.Rising, operand, stored, call.Span) : InvalidRun(call, operand, count.Bound, stored);
    }

    /// <summary>A <c>rising/falling</c> that could not be bound: its operand is still read <paramref name="lag"/> bars further back (0 when the count is no usable number), so the lint still checks it.</summary>
    private static BoundInvalid InvalidRun(Call call, IBoundExpression operand, IBoundExpression count, int lag) => new(call.Span, [operand, count], new LaggedCall(1, lag));
}
