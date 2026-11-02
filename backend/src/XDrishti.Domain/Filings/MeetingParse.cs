namespace XDrishti.Domain.Filings;

/// <summary>The result of reading a board-meeting csv: a file-level reject (<c>bad_header</c>, <c>too_many_rows</c>) or the accepted rows and the rejected ones.</summary>
public sealed record MeetingParse(string? FileReject, IReadOnlyList<MeetingRow> Rows, IReadOnlyList<RowReject> Rejects);
