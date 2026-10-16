namespace XDrishti.Strategies.Binding;

/// <summary><c>abs(x)</c>.</summary>
internal sealed record BoundAbs(IBoundExpression Operand, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Number;
}
