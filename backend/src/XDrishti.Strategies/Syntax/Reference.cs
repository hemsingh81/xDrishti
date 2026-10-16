namespace XDrishti.Strategies.Syntax;

/// <summary>
/// A name used as a value, in one of three spellings: <c>close</c> (just <see cref="Name"/>), <c>prev_day.high</c> (a <see cref="Scope"/> and a name; the binder knows which pairs exist) or
/// <c>tf("1h").close</c> (a <see cref="Timeframe"/> string and a name). <see cref="Scope"/> and <see cref="Timeframe"/> are never both set. The span covers all of it.
/// </summary>
public sealed record Reference(Identifier Name, SourceSpan Span, Identifier? Scope = null, StringLiteral? Timeframe = null) : Expression(Span)
{
    public bool Equals(Reference? other) =>
        other is not null && Name.Equals(other.Name) && Equals(Scope, other.Scope) && Equals(Timeframe, other.Timeframe);

    public override int GetHashCode() => HashCode.Combine(Name, Scope, Timeframe);
}
