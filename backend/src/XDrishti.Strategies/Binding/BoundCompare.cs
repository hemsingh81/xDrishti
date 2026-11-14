using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// A comparison (<see cref="Operator"/> is one of the six: less, less or equal, greater, greater or equal, equal, not equal). Both sides are numbers; or both are true/false values (equal and not equal only). The result is unknown when either side is not ready.
/// </summary>
internal sealed record BoundCompare(BinaryOperator Operator, IBoundExpression Left, IBoundExpression Right, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
