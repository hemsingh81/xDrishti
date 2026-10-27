namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.strategy_draft_decisions</c> (spec P9-07 Data): the owner's decision on a draft (or the auto decision of P9-07 D9), at most one per draft; insert-only.</summary>
internal sealed class AiStrategyDraftDecisionRecord
{
    public long Id { get; set; }

    public long DraftId { get; set; }

    public string Decision { get; set; } = string.Empty;

    public string? StrategyId { get; set; }

    public int? StrategyVersion { get; set; }

    public Guid? LabRunId { get; set; }

    public bool Edited { get; set; }

    public bool Auto { get; set; }

    public long? SettingsId { get; set; }

    public string? SnapshotHash { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string? RunError { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}
