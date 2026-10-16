namespace XDrishti.Strategies.Binding;

/// <summary><c>between(x, low, high)</c>: <c>low &lt;= x and x &lt;= high</c>, both ends included. Unknown when any of the three is not ready.</summary>
internal sealed record BoundBetween(IBoundExpression Value, IBoundExpression Low, IBoundExpression High, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
