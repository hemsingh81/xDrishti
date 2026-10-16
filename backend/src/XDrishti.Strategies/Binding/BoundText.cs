namespace XDrishti.Strategies.Binding;

/// <summary>A string literal. It only exists while the binder looks for the <c>time</c> or <c>regime()</c> it is compared with; a tree that still holds one has a problem.</summary>
internal sealed record BoundText(string Value, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Text;
}
