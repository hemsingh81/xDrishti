namespace XDrishti.Mcp;

/// <summary>A column of a tool result and whether its cells are free text (cleaned, scrubbed) or structured (delivered untouched).</summary>
internal sealed record ToolColumn(string Name, bool IsText);
