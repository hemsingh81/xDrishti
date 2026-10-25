using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Mirror;

internal sealed partial class MirrorRewriter
{
    /// <summary>Two operands brought to one orientation: <see cref="Kind"/> is a typed kind when they mirror with a flip, a plain-like kind when they stay as they are.</summary>
    private readonly record struct Pair(MirrorKind Kind, Expression A, Expression B);

    private static BinaryOperator Flip(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => BinaryOperator.Greater,
        BinaryOperator.Greater => BinaryOperator.Less,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterOrEqual,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessOrEqual,
        _ => op,
    };

    private Typed? BinaryOf(Binary binary)
    {
        var left = Visit(binary.Left);
        var right = left is null ? null : Visit(binary.Right);
        if (left is not { } l || right is not { } r)
        {
            return null;
        }

        switch (binary.Operator)
        {
            case BinaryOperator.And or BinaryOperator.Or:
                return l.Kind == MirrorKind.Bool && r.Kind == MirrorKind.Bool ? new Typed(binary with { Left = l.Tree, Right = r.Tree }, MirrorKind.Bool) : BadTyped();
            case BinaryOperator.Add or BinaryOperator.Subtract:
                return Sum(binary, l, r);
            case BinaryOperator.Multiply or BinaryOperator.Divide:
                return Product(binary, l, r);
            default:
                if (l.Kind == MirrorKind.Bool || r.Kind == MirrorKind.Bool)
                {
                    return BadTyped();
                }

                return Unify(binary, l, r) is { } pair
                    ? new Typed(binary with { Operator = IsTyped(pair.Kind) ? Flip(binary.Operator) : binary.Operator, Left = pair.A, Right = pair.B }, MirrorKind.Bool)
                    : null;
        }
    }

    private Typed? Sum(Binary binary, Typed l, Typed r)
    {
        if (l.Kind == MirrorKind.Bool || r.Kind == MirrorKind.Bool)
        {
            return BadTyped();
        }

        var add = binary.Operator == BinaryOperator.Add;
        if (IsPlainLike(l.Kind) && IsPlainLike(r.Kind))
        {
            return new Typed(binary with { Left = l.Tree, Right = r.Tree }, l.Kind == MirrorKind.Scalar && r.Kind == MirrorKind.Scalar ? MirrorKind.Scalar : MirrorKind.Plain);
        }

        if (l.Kind == MirrorKind.Level && IsPlainLike(r.Kind))
        {
            return new Typed(binary with { Operator = add ? BinaryOperator.Subtract : BinaryOperator.Add, Left = l.Tree, Right = r.Tree }, MirrorKind.Level);
        }

        if (!add && l.Kind == r.Kind && l.Kind is MirrorKind.Level or MirrorKind.Osc)
        {
            return new Typed(binary with { Left = r.Tree, Right = l.Tree }, MirrorKind.Plain);
        }

        if (l.Kind == MirrorKind.Signed && r.Kind == MirrorKind.Signed)
        {
            return new Typed(binary with { Left = l.Tree, Right = r.Tree }, MirrorKind.Signed);
        }

        return Fail(binary, MirrorCodes.NotMirrorable, Quote(binary) + " " + (add ? "adds " : "subtracts ") + Describe(r.Kind) + (add ? " to " : " from ") + Describe(l.Kind) + ", a shape with no single mirror.");
    }

    private Typed? Product(Binary binary, Typed l, Typed r)
    {
        if (l.Kind == MirrorKind.Bool || r.Kind == MirrorKind.Bool)
        {
            return BadTyped();
        }

        var multiply = binary.Operator == BinaryOperator.Multiply;
        if (IsPlainLike(l.Kind) && IsPlainLike(r.Kind))
        {
            return new Typed(binary with { Left = l.Tree, Right = r.Tree }, l.Kind == MirrorKind.Scalar && r.Kind == MirrorKind.Scalar ? MirrorKind.Scalar : MirrorKind.Plain);
        }

        if ((l.Kind == MirrorKind.Signed && r.Kind == MirrorKind.Scalar) || (multiply && l.Kind == MirrorKind.Scalar && r.Kind == MirrorKind.Signed))
        {
            return new Typed(binary with { Left = l.Tree, Right = r.Tree }, MirrorKind.Signed);
        }

        return Fail(
            binary,
            MirrorCodes.NotMirrorable,
            Quote(binary) + " " + (multiply ? "multiplies " : "divides ") + Describe(l.Kind) + (multiply ? " by " : " by ") + Describe(r.Kind) + "; a product or ratio of prices is percent semantics (a 1% rise is not a 1% fall).");
    }

    /// <summary>Brings two operands to one orientation (see <see cref="Pair"/>) or records why it cannot be done.</summary>
    private Pair? Unify(Expression node, Typed a, Typed b)
    {
        if (IsPlainLike(a.Kind) && IsPlainLike(b.Kind))
        {
            return new Pair(a.Kind == MirrorKind.Scalar && b.Kind == MirrorKind.Scalar ? MirrorKind.Scalar : MirrorKind.Plain, a.Tree, b.Tree);
        }

        if (IsTyped(a.Kind) && a.Kind == b.Kind)
        {
            return new Pair(a.Kind, a.Tree, b.Tree);
        }

        if (IsTyped(a.Kind) && b.Kind == MirrorKind.Scalar)
        {
            return Align(node, a.Kind, b.Tree) is { } aligned ? new Pair(a.Kind, a.Tree, aligned) : null;
        }

        if (a.Kind == MirrorKind.Scalar && IsTyped(b.Kind))
        {
            return Align(node, b.Kind, a.Tree) is { } aligned ? new Pair(b.Kind, aligned, b.Tree) : null;
        }

        Fail(node, MirrorCodes.MixedOrientation, Quote(node) + " puts " + Describe(a.Kind) + " against " + Describe(b.Kind) + ", which have no common mirror.");
        return null;
    }

    /// <summary>The scalar as it reads on the mirrored side of a comparison with a value of the given type: negated for a signed value, the complement to 100 for an oscillator, refused for a price.</summary>
    private Expression? Align(Expression node, MirrorKind kind, Expression scalar)
    {
        switch (kind)
        {
            case MirrorKind.Signed:
                return Negate(scalar);
            case MirrorKind.Osc:
                var complement = Complement(scalar);
                if (complement is null)
                {
                    Fail(node, MirrorCodes.LiteralPrecision, Quote(node) + " has a number with more than 15 significant digits, so its complement to 100 cannot be written exactly.");
                }

                return complement;
            default:
                Fail(node, MirrorCodes.AbsoluteLevel, Quote(node) + " compares a price with a bare number; the mirror of an absolute level is not defined.");
                return null;
        }
    }

    private static Expression Negate(Expression scalar)
    {
        switch (scalar)
        {
            case NumberLiteral { Value: 0 }:
            case Unary { Operator: UnaryOperator.Negate, Operand: NumberLiteral { Value: 0 } }:
                return scalar;
            case Unary { Operator: UnaryOperator.Negate } negated:
                return negated.Operand;
            default:
                return new Unary(UnaryOperator.Negate, scalar, scalar.Span);
        }
    }

    private static Expression? Complement(Expression scalar)
    {
        double? value = scalar switch
        {
            NumberLiteral number => number.Value,
            Unary { Operator: UnaryOperator.Negate, Operand: NumberLiteral number } => -number.Value,
            _ => null,
        };
        if (value is { } constant)
        {
            return LiteralMath.Complement(constant) is { } result
                ? result >= 0 ? new NumberLiteral(result, scalar.Span) : new Unary(UnaryOperator.Negate, new NumberLiteral(-result, scalar.Span), scalar.Span)
                : null;
        }

        return scalar is Binary { Operator: BinaryOperator.Subtract, Left: NumberLiteral { Value: 100 } } difference
            ? difference.Right
            : new Binary(BinaryOperator.Subtract, new NumberLiteral(100, scalar.Span), scalar, scalar.Span);
    }

    private Typed? BuiltinCall(Call call, Builtin builtin)
    {
        if (builtin == Builtin.Regime)
        {
            return ContextName(call);
        }

        var expected = builtin switch
        {
            Builtin.Between => 3,
            Builtin.Abs => 1,
            _ => 2,
        };
        if (call.Arguments.Length != expected || call.Arguments.Any(a => a is not PositionalArgument))
        {
            return BadTyped();
        }

        var arguments = call.Arguments.Select(a => ((PositionalArgument)a).Value).ToArray();
        if (builtin == Builtin.Rising || builtin == Builtin.Falling)
        {
            return Slope(call, builtin, arguments);
        }

        var visited = new Typed[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            if (Visit(arguments[i]) is not { } typed)
            {
                return null;
            }

            if (typed.Kind == MirrorKind.Bool)
            {
                return BadTyped();
            }

            visited[i] = typed;
        }

        return builtin switch
        {
            Builtin.CrossesAbove or Builtin.CrossesBelow => Crossing(call, builtin, visited),
            Builtin.Between => Between(call, visited),
            Builtin.Abs => Absolute(call, visited[0]),
            _ => Extreme(call, builtin, visited),
        };
    }

    private static Call Rebuilt(Call call, string name, params Expression[] arguments) =>
        call with { Name = Id(name, call.Name.Span), Arguments = [.. arguments.Select(a => (Argument)new PositionalArgument(a, a.Span))] };

    private Typed? Slope(Call call, Builtin builtin, Expression[] arguments)
    {
        var visited = Visit(arguments[0]);
        if (visited is not { } x)
        {
            return null;
        }

        if (x.Kind == MirrorKind.Bool)
        {
            return BadTyped();
        }

        var name = Vocabulary.NameOf(IsTyped(x.Kind) ? (builtin == Builtin.Rising ? Builtin.Falling : Builtin.Rising) : builtin);
        return new Typed(Rebuilt(call, name, x.Tree, arguments[1]), MirrorKind.Bool);
    }

    private Typed? Crossing(Call call, Builtin builtin, Typed[] v)
    {
        if (Unify(call, v[0], v[1]) is not { } pair)
        {
            return null;
        }

        var name = Vocabulary.NameOf(IsTyped(pair.Kind) ? (builtin == Builtin.CrossesAbove ? Builtin.CrossesBelow : Builtin.CrossesAbove) : builtin);
        return new Typed(Rebuilt(call, name, pair.A, pair.B), MirrorKind.Bool);
    }

    private Typed? Between(Call call, Typed[] v)
    {
        if (Unify(call, v[0], v[1]) is not { } low || Unify(call, v[0], v[2]) is not { } high)
        {
            return null;
        }

        if (IsTyped(low.Kind) != IsTyped(high.Kind))
        {
            return Fail(call, MirrorCodes.MixedOrientation, Quote(call) + " mixes a typed value and a plain one among its bounds, which have no common mirror.");
        }

        return IsTyped(low.Kind)
            ? new Typed(Rebuilt(call, "between", low.A, high.B, low.B), MirrorKind.Bool)
            : new Typed(Rebuilt(call, "between", low.A, low.B, high.B), MirrorKind.Bool);
    }

    private Typed? Absolute(Call call, Typed e) => e.Kind switch
    {
        MirrorKind.Scalar or MirrorKind.Plain => new Typed(Rebuilt(call, "abs", e.Tree), e.Kind),
        MirrorKind.Signed => new Typed(Rebuilt(call, "abs", e.Tree), MirrorKind.Plain),
        _ => Fail(call, MirrorCodes.NotMirrorable, Quote(call) + " takes the absolute value of " + Describe(e.Kind) + "; only a difference of two prices or a signed value has a mirror."),
    };

    private Typed? Extreme(Call call, Builtin builtin, Typed[] v)
    {
        if (Unify(call, v[0], v[1]) is not { } pair)
        {
            return null;
        }

        if (pair.Kind == MirrorKind.Osc)
        {
            return Fail(call, MirrorCodes.NotMirrorable, Quote(call) + " takes the " + (builtin == Builtin.Min ? "smaller" : "larger") + " of two oscillator values, which has no mirror in this table.");
        }

        var name = Vocabulary.NameOf(IsTyped(pair.Kind) ? (builtin == Builtin.Min ? Builtin.Max : Builtin.Min) : builtin);
        return new Typed(Rebuilt(call, name, pair.A, pair.B), pair.Kind);
    }
}
