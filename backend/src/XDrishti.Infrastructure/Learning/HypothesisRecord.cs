namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.hypotheses</c> (spec P6-10, ADR 0076). Insert-only; the proposing row has no <see cref="HypothesisId"/> (its own id is the hypothesis id) and every later row repeats the facts with a new state.</summary>
internal sealed class HypothesisRecord
{
    public long Id { get; set; }

    public long? HypothesisId { get; set; }

    public string State { get; set; } = string.Empty;

    public string? FromState { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? CellKey { get; set; }

    public string KnobName { get; set; } = string.Empty;

    public string KnobKind { get; set; } = string.Empty;

    public string BaselineValue { get; set; } = string.Empty;

    public string CandidateValue { get; set; } = string.Empty;

    public string Rationale { get; set; } = string.Empty;

    public string BaselineRunKey { get; set; } = string.Empty;

    public string? CandidateRunKey { get; set; }

    public long? ExperimentId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
