namespace XDrishti.Application.Abstractions.Filings;

/// <summary>One row of <c>filings.result_parses</c>.</summary>
public sealed record StoredParse(Guid FilingId, string ParserVersion, string Outcome, string? Reason, int Facts, IReadOnlyDictionary<string, int> Flags);
