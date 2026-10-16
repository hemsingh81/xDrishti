namespace XDrishti.Strategies.Syntax;

/// <summary>The keyword <c>true</c> or <c>false</c>.</summary>
public sealed record BoolLiteral(bool Value, SourceSpan Span) : Expression(Span)
{
    public bool Equals(BoolLiteral? other) => other is not null && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();
}
