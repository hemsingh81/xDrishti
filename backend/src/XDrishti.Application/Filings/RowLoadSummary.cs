using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>One csv file of a loader run (<c>Rejects</c>: the first 50 rejected rows or groups with line, closed reason and column name, never a cell content): how many data rows it held, how many were stored, duplicate or rejected, and the file-level reject (<c>bad_header</c>, <c>too_many_rows</c>, <c>content_changed</c>, <c>unreadable</c>) if the whole file was refused. The name is for the caller; it is never logged.</summary>
public sealed record RowLoadFile(string FileName, string? FileReject, int Rows, int Loaded, int Duplicate, int Rejected, IReadOnlyList<RowReject> Rejects);

/// <summary>
/// What one loader run did, as counts only (P15-04 criterion 8). <see cref="Loaded"/> counts stored envelopes (rows for the calendar, groups for shareholding); <see cref="Rejected"/> counts by closed reason (<c>bad_header</c>, <c>too_many_rows</c>, <c>bad_row</c>,
/// <c>unknown_instrument</c>, <c>ambiguous_symbol</c>, <c>inconsistent_ts</c>, <c>out_of_range</c>, <c>ambiguous_order</c>, <c>bad_supersede</c>, <c>content_changed</c>, <c>unreadable</c>); a rejected file counts once under its reason.
/// </summary>
public sealed record RowLoadSummary(
    string Kind,
    int Files,
    int Loaded,
    int Revisions,
    int OutOfOrder,
    int Duplicate,
    int Unavailable,
    int SumOff,
    IReadOnlyDictionary<string, int> Rejected,
    int Failures,
    IReadOnlyList<RowLoadFile> PerFile)
{
    /// <summary>The first 10 rejects over the files, with line and column.</summary>
    private IEnumerable<RowReject> FirstRejectsSource => PerFile.SelectMany(f => f.Rejects);

    private IReadOnlyList<RowReject> FirstRejects => [.. FirstRejectsSource.Take(10)];

    /// <summary>The one counts line of the run (no file name, no cell text).</summary>
    public string Text =>
        $"{Kind}: files {Files}, loaded {Loaded} (revisions {Revisions}, out of order {OutOfOrder}, without an intimation time {Unavailable}), duplicate {Duplicate}, rejected {Rejected.Values.Sum()}"
        + (Rejected.Count > 0 ? " (" + string.Join(", ", Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key} {r.Value}")) + ")" : string.Empty)
        + (FirstRejects.Count > 0 ? ", first rejects: " + string.Join("; ", FirstRejects.Select(r => $"line {r.Line} {r.Reason}" + (r.Column is null ? string.Empty : $" ({r.Column})"))) : string.Empty)
        + (SumOff > 0 ? $", sum off {SumOff}" : string.Empty)
        + (Failures > 0 ? $", {Failures} failed and left for the next run" : string.Empty);
}
