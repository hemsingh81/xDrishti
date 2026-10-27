using System.Text.Json;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A stored tool call with its envelope (up to 64 KB), as the audit keeps it.</summary>
public sealed record StoredToolCall(
    long Id, Guid ConversationId, Guid TurnId, int CallIndex, string Tool, JsonElement Arguments, string Status, string? ErrorCode, JsonElement? Result, int ResultBytes, int Rows, bool Truncated, JsonElement? Evidence, int Redactions, int InjectionFlags, DateTimeOffset StartedAt, int DurationMs);
