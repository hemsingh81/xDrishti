namespace XDrishti.Application.Filings;

/// <summary>
/// What one ingest run did, as counts only. <see cref="Rejected"/> counts by closed reason (<c>unknown_instrument</c>, <c>ambiguous_symbol</c>, <c>ambiguous_order</c>, <c>bad_supersede</c>, <c>content_changed</c>, <c>unreadable</c>, <c>store_conflict</c>, plus the
/// reasons the inbox listing itself gave). <see cref="Stopped"/> is <c>disk_budget</c> or <c>disk_free</c> when the disk guard ended the run.
/// </summary>
public sealed record FilingIngestSummary(
    int Listed,
    int Stored,
    int Revisions,
    int OutOfOrder,
    int Duplicate,
    int SidecarConflict,
    int Unavailable,
    int DeferredToLoader,
    IReadOnlyDictionary<string, int> Rejected,
    IReadOnlyList<string> Problems,
    int Failures,
    string? Stopped)
{
    /// <summary>The one counts line of the run.</summary>
    public string Text =>
        $"listed {Listed}, stored {Stored} (revisions {Revisions}, out of order {OutOfOrder}, without a broadcast time {Unavailable}), duplicate {Duplicate}, sidecar conflict {SidecarConflict}, deferred to loader {DeferredToLoader}, rejected {Rejected.Values.Sum()}"
        + (Rejected.Count > 0 ? " (" + string.Join(", ", Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key} {r.Value}")) + ")" : string.Empty)
        + (Failures > 0 ? $", {Failures} failed and left for the next run" : string.Empty)
        + (Problems.Count > 0 ? ", inbox problems: " + string.Join(", ", Problems) : string.Empty)
        + (Stopped is not null ? $", stopped: {Stopped}" : string.Empty);
}
