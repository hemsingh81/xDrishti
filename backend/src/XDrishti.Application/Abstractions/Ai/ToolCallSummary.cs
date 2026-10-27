using System.Text.Json;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A tool call as the conversation read shows it under the answer (the stored envelope is read with <c>GET /api/assistant/tool-calls/{id}</c>).</summary>
public sealed record ToolCallSummary(long Id, int Index, string Tool, JsonElement Arguments, string Status, string? ErrorCode, int Rows, bool Truncated, int DurationMs, JsonElement? Evidence);
