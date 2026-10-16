namespace XDrishti.Strategies.Syntax;

/// <summary>A name as written: letters, digits and underscores, not starting with a digit (<c>rsi</c>, <c>prev_day</c>, <c>MON</c>). The text keeps the case; the binder is case-sensitive.</summary>
public sealed record Identifier(string Text, SourceSpan Span) : SyntaxNode(Span)
{
    public bool Equals(Identifier? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
}
