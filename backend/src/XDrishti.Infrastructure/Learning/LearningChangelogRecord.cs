namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.learning_changelog</c> (spec P6-08, ADR 0073). Insert-only; a revert is a row whose <see cref="RevertsId"/> points at the reverted one.</summary>
internal sealed class LearningChangelogRecord
{
    public long Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string SubjectKind { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string FromState { get; set; } = string.Empty;

    public string ToState { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string[] EvidenceIds { get; set; } = [];

    public string? BeforeRef { get; set; }

    public string? AfterRef { get; set; }

    public int? PaperDays { get; set; }

    public long? RevertsId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
