namespace XDrishti.Strategies.Binding;

/// <summary><c>min(a, b)</c> or <c>max(a, b)</c> (<see cref="IsMax"/>). Not ready (NaN) when either side is.</summary>
internal sealed record BoundMinMax(bool IsMax, IBoundExpression Left, IBoundExpression Right, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Number;
}
