namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A tool call the model asked for: its id, the tool name and the JSON text of the arguments.</summary>
public sealed record ModelToolCall(string Id, string Name, string ArgumentsJson);
