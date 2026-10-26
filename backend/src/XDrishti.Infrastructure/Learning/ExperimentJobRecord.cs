namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.experiment_jobs</c> (spec P8-18 Part B, ADR 0129): a replay job the owner started. The runtime role inserts it and updates only its state columns; it never deletes, and a trigger freezes a final state.</summary>
internal sealed class ExperimentJobRecord
{
    public Guid Id { get; set; }

    public string JobKey { get; set; } = string.Empty;

    public string BaselineRunKey { get; set; } = string.Empty;

    public string KnobKind { get; set; } = string.Empty;

    public string KnobName { get; set; } = string.Empty;

    public string BaselineValue { get; set; } = string.Empty;

    public string CandidateValue { get; set; } = string.Empty;

    public int HoldOutMonths { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? Stage { get; set; }

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    public bool CancelRequested { get; set; }

    public string? CandidateRunKey { get; set; }

    public long? ExperimentId { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorText { get; set; }

    public int Instruments { get; set; }

    public long? ReplayMs { get; set; }

    public long? CompareMs { get; set; }

    public bool OtherHeavyJobsSeen { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Host { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
