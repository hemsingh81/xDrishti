namespace XDrishti.Strategies.Syntax;

/// <summary>An argument given by name, <c>source=high</c>: the value is a bare name (here <c>high</c>, which the binder reads as a price source, not as the feature of that name).</summary>
public sealed record NamedArgument(Identifier Name, Identifier Value, SourceSpan Span) : Argument(Span)
{
    public bool Equals(NamedArgument? other) => other is not null && Name.Equals(other.Name) && Value.Equals(other.Value);

    public override int GetHashCode() => HashCode.Combine(Name, Value);
}
