namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.conversations</c> (spec P9-03): one row per conversation, written when its first turn starts. Insert-only.</summary>
internal sealed class AiConversationRecord
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Origin { get; set; } = string.Empty;

    public long? SettingsId { get; set; }

    public string ModelId { get; set; } = string.Empty;

    public string? LlmBuild { get; set; }
}
