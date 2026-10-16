namespace XDrishti.Strategies.Syntax;

/// <summary>An argument that is an expression: the <c>14</c> of <c>rsi(14)</c>.</summary>
public sealed record PositionalArgument(Expression Value, SourceSpan Span) : Argument(Span)
{
    public bool Equals(PositionalArgument? other) => other is not null && Value.Equals(other.Value);

    public override int GetHashCode() => Value.GetHashCode();
}
