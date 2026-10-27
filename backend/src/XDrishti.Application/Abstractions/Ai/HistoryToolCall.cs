namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A tool call of an earlier turn as the history shows it to the model: the tool, the arguments, how it ended and how many rows. Never the result.</summary>
public sealed record HistoryToolCall(int CallIndex, string Tool, string ArgumentsJson, string Status, int Rows);
