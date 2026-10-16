namespace XDrishti.Strategies.Binding;

/// <summary>
/// <c>crosses_above(a, b)</c> (<see cref="IsAbove"/>) or <c>crosses_below(a, b)</c>: <c>a[0] &gt; b[0] and a[1] &lt;= b[1]</c>, respectively <c>a[0] &lt; b[0] and a[1] &gt;= b[1]</c>, where each side is an expression
/// evaluated at lag 0 and at lag 1 (every feature in it is read one more closed bar back). Unknown when any of the four values is not ready.
/// </summary>
internal sealed record BoundCrossing(bool IsAbove, IBoundExpression Left, IBoundExpression Right, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
