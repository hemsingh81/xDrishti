namespace XDrishti.Strategies.Binding;

/// <summary>The keyword <c>true</c> or <c>false</c>.</summary>
internal sealed record BoundBool(bool Value, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
