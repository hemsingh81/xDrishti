namespace XDrishti.Domain.Filings;

/// <summary>The result of reading an announcements csv: a file-level reject (<c>bad_header</c>, <c>too_many_rows</c>) or the accepted rows and the rejected ones.</summary>
public sealed record AnnouncementParse(string? FileReject, IReadOnlyList<AnnouncementRow> Rows, IReadOnlyList<RowReject> Rejects);
