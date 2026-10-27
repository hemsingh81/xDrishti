namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One message of the conversation sent to the model. An assistant message may carry the tool calls it made; a tool message carries the id of the call it answers.</summary>
public sealed record ModelMessage(ModelRole Role, string Content, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? ToolCallId = null);
