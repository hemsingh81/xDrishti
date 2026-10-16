using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary><c>a and b</c> or <c>a or b</c> on two true/false values (<see cref="Operator"/> is <see cref="BinaryOperator.And"/> or <see cref="BinaryOperator.Or"/>); the three-valued tables apply.</summary>
internal sealed record BoundLogical(BinaryOperator Operator, IBoundExpression Left, IBoundExpression Right, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
