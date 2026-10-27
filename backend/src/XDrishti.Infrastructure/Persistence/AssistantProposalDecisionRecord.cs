namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>workflow.assistant_proposal_decisions</c> (spec P9-08 Data): the decision on a proposal, at most one per proposal; insert-only. An auto decision carries the settings row id and hash and is never an owner approval.</summary>
internal sealed class AssistantProposalDecisionRecord
{
    public long Id { get; set; }

    public long ProposalId { get; set; }

    public string Decision { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public bool Auto { get; set; }

    public long? SettingsId { get; set; }

    public string? SnapshotHash { get; set; }

    public string? Reason { get; set; }

    public string? ResultRef { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}
