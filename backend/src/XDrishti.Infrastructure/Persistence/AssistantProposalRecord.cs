namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>workflow.assistant_proposals</c> (spec P9-08 Data): one row per proposal that passed every engine check; insert-only. The payload, the engine's evidence and checks, the pack hash and the model that made it.</summary>
internal sealed class AssistantProposalRecord
{
    public long Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string DedupeKey { get; set; } = string.Empty;

    public DateOnly CreatedOn { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string EvidenceJson { get; set; } = string.Empty;

    public string ChecksJson { get; set; } = string.Empty;

    public string PackHash { get; set; } = string.Empty;

    public string? ModelId { get; set; }

    public string? ModelSha256 { get; set; }

    public string PromptVersion { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
