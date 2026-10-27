namespace XDrishti.Domain.Assistant;

/// <summary>The error part of a tool envelope.</summary>
public sealed record ToolError(string Code, string Message);
