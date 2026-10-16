namespace XDrishti.Strategies.Syntax;

/// <summary>
/// A string in double quotes (<c>"09:45"</c>, <c>"1h"</c>, <c>"UP_LOW_VOL"</c>); <see cref="Value"/> is the text between the quotes. A string has no escapes: it cannot contain a double quote or a line break, so a
/// value with either one cannot be printed.
/// </summary>
public sealed record StringLiteral(string Value, SourceSpan Span) : Expression(Span)
{
    public bool Equals(StringLiteral? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}
