namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A changelog row to append (spec P6-08).</summary>
/// <param name="Kind"><c>transition</c>, <c>designation</c> or <c>revert</c>.</param>
/// <param name="SubjectKind"><c>cell</c> or <c>model</c>.</param>
/// <param name="Subject">The cell key, or <c>model:ID</c>.</param>
/// <param name="FromState">The state (or role) before.</param>
/// <param name="ToState">The state (or role) after.</param>
/// <param name="Reason">Why, at most 500 characters.</param>
/// <param name="Actor">Who or what did it.</param>
/// <param name="EvidenceIds">Ids of what justifies it (runs, cell statistics, verdicts, rows).</param>
/// <param name="BeforeRef">A reference to the thing before, or null.</param>
/// <param name="AfterRef">A reference to the thing after, or null.</param>
/// <param name="PaperDays">Paper days stated for Paper to Active, or null.</param>
/// <param name="RevertsId">The row this reverts, or null.</param>
/// <param name="At">When.</param>
public sealed record NewChangelogEntry(
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
    DateTimeOffset At);
