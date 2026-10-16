namespace XDrishti.Strategies.Binding;

/// <summary><c>not x</c> on a true/false value. <i>Unknown</i> stays unknown.</summary>
internal sealed record BoundNot(IBoundExpression Operand, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
