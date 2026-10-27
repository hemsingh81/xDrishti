namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.eval_cases</c> (spec P9-09 Contract): one row per case and repeat of a run; insert-only. The answer is the assistant's answer to a fixture question; <c>session_id</c> is the conversation of the audit.</summary>
internal sealed class AiEvalCaseRecord
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public string Set { get; set; } = string.Empty;

    public string CaseId { get; set; } = string.Empty;

    public int Repeat { get; set; }

    public bool Passed { get; set; }

    public bool? Grounded { get; set; }

    public decimal? Score { get; set; }

    public string DetailJson { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public Guid? SessionId { get; set; }

    public int ToolCalls { get; set; }

    public int LatencyMs { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }
}
