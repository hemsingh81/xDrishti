namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.strategy_drafts</c> (spec P9-07 Data): one row per finished drafting job; insert-only. The idea, every attempt, the model and prompt that made it. The row is written once, when the job ends.</summary>
internal sealed class AiStrategyDraftRecord
{
    public long Id { get; set; }

    public string Idea { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? FailReason { get; set; }

    public string Via { get; set; } = string.Empty;

    public Guid? BasketId { get; set; }

    public string? Yaml { get; set; }

    public string AnswerJson { get; set; } = string.Empty;

    public string AttemptsJson { get; set; } = string.Empty;

    public string ProblemsJson { get; set; } = string.Empty;

    public string FindingsJson { get; set; } = string.Empty;

    public string? ModelId { get; set; }

    public string? ModelSha256 { get; set; }

    public string PromptVersion { get; set; } = string.Empty;

    public string SheetHash { get; set; } = string.Empty;

    public string ParamsJson { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
