namespace XDrishti.Domain.Filings;

/// <summary>One record of a csv: the line it starts on (1-based) and its fields, or null fields when the record is malformed (a bad quote).</summary>
public sealed record CsvRecord(int Line, IReadOnlyList<string>? Fields);
