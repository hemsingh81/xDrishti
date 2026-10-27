namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One tool call to store. <paramref name="ResultJson"/> is already capped (64 KB); <paramref name="ArgumentsJson"/> is valid JSON (an invalid text is stored as a JSON string).</summary>
public sealed record ToolCallRecord(
    Guid ConversationId, Guid TurnId, int CallIndex, string Tool, string ArgumentsJson, string Status, string? ErrorCode, string? ResultJson, int ResultBytes, int Rows, bool Truncated, string? EvidenceJson, int Redactions, int InjectionFlags, DateTimeOffset StartedAt, int DurationMs);
