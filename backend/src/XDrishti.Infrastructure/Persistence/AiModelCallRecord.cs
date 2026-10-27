namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.model_calls</c> (spec P9-03): a model call outside chat (briefing, draft, proposal, eval); no content. Insert-only.</summary>
internal sealed class AiModelCallRecord
{
    public long Id { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string Caller { get; set; } = string.Empty;

    public string PromptHash { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string? ModelSha256 { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int TotalMs { get; set; }

    public string FinishReason { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
