using System.Runtime.CompilerServices;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// Turns the syntax tree of a condition into a bound tree (spec P3-02, <i>Names</i>, <i>Timeframes</i> and <i>Types</i>): resolves every name (a strategy parameter, then a builtin or constant or <c>time</c>, then a catalog
/// feature), maps the arguments of a feature to the catalog's parameters (positionally, with the catalog's defaults, and the named <c>source=</c>), qualifies a feature with its timeframe, builds the canonical
/// <see cref="Indicators.FeatureSpec"/> and types every node. Problems go to the <see cref="BindingScope"/> and the part that failed becomes a <see cref="BoundInvalid"/>, so that all problems of a condition are found
/// in one pass and one mistake does not bring a cascade. The class is split by subject: this file holds the entry point and the operators; <c>Binder.Comparisons</c>, <c>Binder.Names</c>, <c>Binder.Features</c> and
/// <c>Binder.Builtins</c> hold the rest. One instance binds one condition on one thread.
/// </summary>
internal sealed partial class Binder(BindingScope scope)
{
    /// <summary>Binds the whole condition, which must be a true/false value.</summary>
    public IBoundExpression BindCondition(Expression root)
    {
        var bound = Bind(root);
        if (bound.Type is BoundType.Bool or BoundType.Invalid)
        {
            return bound;
        }

        scope.Error(
            LintCodes.TypeMismatch,
            "A condition must be true or false, but " + scope.Quoted(root.Span) + " is " + Describe.Type(bound.Type) + "." + ConditionHint(bound),
            root.Span);
        return new BoundInvalid(root.Span, [bound]);
    }

    /// <summary>Binds an expression; never null. Recursion follows the syntax tree, whose depth the parser limits, and checks that the stack has room.</summary>
    private IBoundExpression Bind(Expression expression)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        return expression switch
        {
            NumberLiteral number => new BoundNumber(number.Value, number.Span),
            BoolLiteral boolean => new BoundBool(boolean.Value, boolean.Span),
            StringLiteral text => new BoundText(text.Value, text.Span),
            Reference reference => BindReference(reference),
            Subscript subscript => BindSubscript(subscript),
            Call call => BindCall(call),
            Unary unary => BindUnary(unary),
            Binary binary => BindBinary(binary),
            _ => throw new ArgumentException("Unknown expression node " + expression.GetType().Name + ".", nameof(expression)),
        };
    }

    private IBoundExpression BindUnary(Unary unary)
    {
        var operand = Bind(unary.Operand);
        if (unary.Operator == UnaryOperator.Not)
        {
            return Expect(operand, BoundType.Bool, "`not` needs a true/false value, but ") ? new BoundNot(operand, unary.Span) : new BoundInvalid(unary.Span, [operand]);
        }

        return Expect(operand, BoundType.Number, "`-` needs a number, but ") ? new BoundNegate(operand, unary.Span) : new BoundInvalid(unary.Span, [operand]);
    }

    private IBoundExpression BindBinary(Binary binary)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.And or BinaryOperator.Or:
                {
                    var (left, right, ok) = BindOperands(binary, BoundType.Bool);
                    return ok ? new BoundLogical(binary.Operator, left, right, binary.Span) : new BoundInvalid(binary.Span, [left, right]);
                }

            case BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide:
                {
                    var (left, right, ok) = BindOperands(binary, BoundType.Number);
                    return ok ? new BoundArithmetic(binary.Operator, left, right, binary.Span) : new BoundInvalid(binary.Span, [left, right]);
                }

            default:
                return BindComparison(binary);
        }
    }

    /// <summary>Binds both sides of an operator that needs one type on both; a side of another type is reported (both are, when both are wrong) and <c>Ok</c> is false.</summary>
    private (IBoundExpression Left, IBoundExpression Right, bool Ok) BindOperands(Binary binary, BoundType expected)
    {
        var left = Bind(binary.Left);
        var right = Bind(binary.Right);
        var symbol = "`" + Describe.Symbol(binary.Operator) + "`";
        var needs = expected == BoundType.Bool ? "a true/false value" : "a number";
        var leftOk = Expect(left, expected, "The left side of " + symbol + " needs " + needs + ", but ");
        var rightOk = Expect(right, expected, "The right side of " + symbol + " needs " + needs + ", but ");
        return (left, right, leftOk && rightOk);
    }

    /// <summary>
    /// True when the node has the expected type. A node that could not be bound is not true (and says nothing more: its problem is reported); a node of another type is reported as a type mismatch, with
    /// <paramref name="beginning"/> (which ends before the quoted text) and the name of the type it has.
    /// </summary>
    private bool Expect(IBoundExpression node, BoundType expected, string beginning)
    {
        if (node.Type == expected)
        {
            return true;
        }

        if (node.Type != BoundType.Invalid)
        {
            scope.Error(LintCodes.TypeMismatch, beginning + scope.Quoted(node.Span) + " is " + Describe.Type(node.Type) + "." + Describe.Hint(node.Type), node.Span);
        }

        return false;
    }

    private static string ConditionHint(IBoundExpression bound) => bound.Type switch
    {
        BoundType.Number => " Compare it with something, like rsi(14) > 55.",
        _ => Describe.Hint(bound.Type),
    };
}
