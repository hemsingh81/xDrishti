using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>An arithmetic operator on two numbers (<see cref="Operator"/> is add, subtract, multiply or divide). A division by zero is not ready (NaN), never an exception.</summary>
internal sealed record BoundArithmetic(BinaryOperator Operator, IBoundExpression Left, IBoundExpression Right, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Number;
}
