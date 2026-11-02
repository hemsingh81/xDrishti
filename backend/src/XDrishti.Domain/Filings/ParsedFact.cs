namespace XDrishti.Domain.Filings;

/// <summary>One fact of a parsed results file: a field for a span and a basis, in a unit. <see cref="Value"/> is null exactly when the fact is unavailable, and then <see cref="Reason"/> says why.</summary>
public sealed record ParsedFact(string Field, string Span, string Basis, decimal? Value, string Unit, string? Reason)
{
    public bool IsValue => Value is not null;

    public static ParsedFact Of(string field, string span, string basis, decimal value) => new(field, span, basis, value, ResultCodes.UnitOf(field), null);

    public static ParsedFact Missing(string field, string span, string basis, string reason) => new(field, span, basis, null, ResultCodes.UnitOf(field), reason);
}
