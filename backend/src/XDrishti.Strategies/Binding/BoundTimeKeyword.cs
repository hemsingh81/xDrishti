namespace XDrishti.Strategies.Binding;

/// <summary>The keyword <c>time</c> on its own. It only exists while the binder looks for the text it is compared with (see <see cref="BoundTimeCompare"/>); a tree that still holds one has a problem.</summary>
internal sealed record BoundTimeKeyword(SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Time;
}
