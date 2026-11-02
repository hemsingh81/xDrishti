namespace XDrishti.Domain.Filings;

/// <summary>The result facts known at an instant (P15-03): every answered fact (period newest first, then span, basis and field in fixed order) and the number of results filings of the instrument with no broadcast time (counted, never shown).</summary>
public sealed record ResultFactsAnswer(IReadOnlyList<ResultFactAnswer> Facts, int UndatedCount)
{
    /// <summary>The parser version the facts were read for (set by the reader port); a consumer carries it, so facts of a synthetic parser stay visibly that.</summary>
    public string ParserVersion { get; init; } = string.Empty;

    /// <summary>The answer for one key; a key never seen is <c>unavailable/not_in_file</c>, never zero.</summary>
    public ResultFactAnswer Get(DateOnly periodEnd, string field, string span, string basis) =>
        Facts.FirstOrDefault(f => f.PeriodEnd == periodEnd && f.Field == field && f.Span == span && f.Basis == basis)
        ?? new ResultFactAnswer(periodEnd, field, span, basis, "unavailable", null, ResultCodes.UnitOf(field), ResultCodes.NotInFile, null, null);
}
