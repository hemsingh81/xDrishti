namespace XDrishti.Domain.Filings;

/// <summary>The result of reading a shareholding csv: a file-level reject (<c>bad_header</c>, <c>too_many_rows</c>) or the accepted rows and the rejected ones.</summary>
public sealed record ShareholdingParse(string? FileReject, IReadOnlyList<ShareholdingRow> Rows, IReadOnlyList<RowReject> Rejects);
