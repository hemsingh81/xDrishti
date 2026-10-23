using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>A changelog row as the API shows it (P6-08): what changed, by whom, why, before and after, and the evidence. A revert has <see cref="RevertsId"/>.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Kind">transition, designation or revert.</param>
/// <param name="SubjectKind">cell or model.</param>
/// <param name="Subject">The cell key, or model:ID.</param>
/// <param name="FromState">The state or role before.</param>
/// <param name="ToState">The state or role after.</param>
/// <param name="Reason">Why.</param>
/// <param name="Actor">Who or what.</param>
/// <param name="EvidenceIds">Evidence ids.</param>
/// <param name="BeforeRef">Reference to the thing before, or null.</param>
/// <param name="AfterRef">Reference to the thing after, or null.</param>
/// <param name="PaperDays">Paper days stated, or null.</param>
/// <param name="RevertsId">The reverted row, or null.</param>
/// <param name="At">When.</param>
public sealed record ChangelogEntryView(
    long Id,
    string Kind,
    string SubjectKind,
    string Subject,
    string FromState,
    string ToState,
    string Reason,
    string Actor,
    IReadOnlyList<string> EvidenceIds,
    string? BeforeRef,
    string? AfterRef,
    int? PaperDays,
    long? RevertsId,
    DateTimeOffset At)
{
    /// <summary>The view of a stored row.</summary>
    public static ChangelogEntryView Of(ChangelogEntry row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var e = row.Entry;
        return new ChangelogEntryView(row.Id, e.Kind, e.SubjectKind, e.Subject, e.FromState, e.ToState, e.Reason, e.Actor, e.EvidenceIds, e.BeforeRef, e.AfterRef, e.PaperDays, e.RevertsId, e.At);
    }
}
