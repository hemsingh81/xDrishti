namespace XDrishti.Application.Abstractions.Ai;

/// <summary>What one tool call came back with: <paramref name="Reachable"/> false when the tool server could not be reached (nothing was called), else the text the tool returned (the JSON envelope of the contract) and whether the server flagged an error.</summary>
public sealed record ToolInvocation(bool Reachable, bool IsError, string Text);
