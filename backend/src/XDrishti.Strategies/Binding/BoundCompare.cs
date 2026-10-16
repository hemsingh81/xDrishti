using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// A comparison (<see cref="Operator"/> is one of the six: less, less or equal, greater, greater or equal, equal, not equal). Both sides are numbers; or both are true/false values (equal and not equal only); or the
/// left side is <c>regime()</c> (a <see cref="BoundType.Regime"/> feature) and the right side the code 1 to 9 as a <see cref="BoundNumber"/> (equal and not equal only): <c>regime() == "UP_LOW_VOL"</c> is bound to
/// this node with the code 1, and <c>"UP_LOW_VOL" == regime()</c> is turned around to the same node. The result is unknown when either side is not ready.
/// </summary>
internal sealed record BoundCompare(BinaryOperator Operator, IBoundExpression Left, IBoundExpression Right, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
