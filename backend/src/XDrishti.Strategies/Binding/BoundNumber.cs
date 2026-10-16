namespace XDrishti.Strategies.Binding;

/// <summary>A number: a literal, or the value of a strategy parameter or of a constant such as <c>TUE</c> (2), known when the condition is compiled.</summary>
internal sealed record BoundNumber(double Value, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Number;
}
