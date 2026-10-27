namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.messages</c> (spec P9-03): the user's message when a turn starts, the assistant's message once at the end. Insert-only.</summary>
internal sealed class AiMessageRecord
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid TurnId { get; set; }

    public int Seq { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string? PromptHash { get; set; }

    public string? ModelId { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int? FirstTokenMs { get; set; }

    public int? TotalMs { get; set; }

    public string? FinishReason { get; set; }

    public string? GroundingStatus { get; set; }

    public string? Ungrounded { get; set; }
}
