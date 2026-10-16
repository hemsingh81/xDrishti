namespace XDrishti.Strategies.Binding;

/// <summary>A minus sign in front of a number.</summary>
internal sealed record BoundNegate(IBoundExpression Operand, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Number;
}
