namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A stored message for the history window (oldest first). An assistant message carries the calls of its turn.</summary>
public sealed record HistoryMessage(string Role, string Content, IReadOnlyList<HistoryToolCall> ToolCalls);
