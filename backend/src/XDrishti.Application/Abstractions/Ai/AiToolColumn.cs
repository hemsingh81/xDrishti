namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A column of a tool result: its name and whether its cells are free text (cleaned, scrubbed and cut by the tool runner) or structured values (delivered untouched).</summary>
public sealed record AiToolColumn(string Name, bool IsText);
