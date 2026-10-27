namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.tool_calls</c> (spec P9-03): one row per tool call of a turn with its arguments and the capped result envelope (at most 64 KB). Insert-only.</summary>
internal sealed class AiToolCallRecord
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid TurnId { get; set; }

    public int CallIndex { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public string Arguments { get; set; } = "{}";

    public string Status { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? Result { get; set; }

    public int ResultBytes { get; set; }

    public int Rows { get; set; }

    public bool Truncated { get; set; }

    public string? Evidence { get; set; }

    public int Redactions { get; set; }

    public int InjectionFlags { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public int DurationMs { get; set; }
}
