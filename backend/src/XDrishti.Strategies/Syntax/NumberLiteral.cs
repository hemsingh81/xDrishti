namespace XDrishti.Strategies.Syntax;

/// <summary>A decimal number as written (<c>55</c>, <c>0.5</c>, <c>1e-3</c>): always finite and never negative when it comes from the parser (a minus sign is a <see cref="Unary"/> of <see cref="UnaryOperator.Negate"/>).</summary>
public sealed record NumberLiteral(double Value, SourceSpan Span) : Expression(Span)
{
    public bool Equals(NumberLiteral? other) => other is not null && Value.Equals(other.Value);

    public override int GetHashCode() => Value.GetHashCode();
}
