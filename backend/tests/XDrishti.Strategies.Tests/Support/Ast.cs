using System.Collections.Immutable;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Short constructors for the expected trees of the syntax tests. The spans are left empty: node equality does not look at positions, and the tests that do check positions say so.</summary>
public static class Ast
{
    private static SourceSpan NoSpan => default;

    public static NumberLiteral N(double value) => new(value, NoSpan);

    public static StringLiteral S(string value) => new(value, NoSpan);

    public static BoolLiteral B(bool value) => new(value, NoSpan);

    public static Identifier Id(string text) => new(text, NoSpan);

    /// <summary><c>close</c>.</summary>
    public static Reference R(string name) => new(Id(name), NoSpan);

    /// <summary><c>prev_day.high</c>.</summary>
    public static Reference R(string scope, string name) => new(Id(name), NoSpan, Id(scope));

    /// <summary><c>tf("1h").close</c>.</summary>
    public static Reference Tf(string frame, string name) => new(Id(name), NoSpan, null, S(frame));

    /// <summary>A call; each argument is an <see cref="Expression"/> (positional) or an <see cref="Argument"/>.</summary>
    public static Call C(string name, params object[] arguments) => new(Id(name), Arguments(arguments), NoSpan);

    /// <summary><c>tf("1h").ema(20)</c>.</summary>
    public static Call TfC(string frame, string name, params object[] arguments) => new(Id(name), Arguments(arguments), NoSpan, S(frame));

    /// <summary><c>source=high</c>.</summary>
    public static NamedArgument Named(string name, string value) => new(Id(name), Id(value), NoSpan);

    /// <summary><c>close[1]</c> or <c>highest(20)[1]</c>.</summary>
    public static Subscript Idx(Expression target, int lag) => new(target, lag, NoSpan, NoSpan);

    public static Unary Not(Expression operand) => new(UnaryOperator.Not, operand, NoSpan);

    public static Unary Neg(Expression operand) => new(UnaryOperator.Negate, operand, NoSpan);

    public static Binary Bin(BinaryOperator op, Expression left, Expression right) => new(op, left, right, NoSpan);

    public static Binary Or(Expression left, Expression right) => Bin(BinaryOperator.Or, left, right);

    public static Binary And(Expression left, Expression right) => Bin(BinaryOperator.And, left, right);

    public static Binary Lt(Expression left, Expression right) => Bin(BinaryOperator.Less, left, right);

    public static Binary Gt(Expression left, Expression right) => Bin(BinaryOperator.Greater, left, right);

    public static Binary Add(Expression left, Expression right) => Bin(BinaryOperator.Add, left, right);

    public static Binary Sub(Expression left, Expression right) => Bin(BinaryOperator.Subtract, left, right);

    public static Binary Mul(Expression left, Expression right) => Bin(BinaryOperator.Multiply, left, right);

    public static Binary Div(Expression left, Expression right) => Bin(BinaryOperator.Divide, left, right);

    private static ImmutableArray<Argument> Arguments(object[] arguments) =>
        [.. arguments.Select(a => a switch
        {
            Argument argument => argument,
            Expression expression => new PositionalArgument(expression, NoSpan),
            _ => throw new ArgumentException("An argument is an Expression or an Argument, not " + a.GetType().Name),
        })];
}
