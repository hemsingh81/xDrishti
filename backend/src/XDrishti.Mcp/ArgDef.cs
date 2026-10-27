namespace XDrishti.Mcp;

/// <summary>One argument of a tool: the single source of its JSON Schema and of its validation (type, bounds, pattern, and for a map the allowed keys), so the schema the model sees is the check the server makes.</summary>
internal sealed record ArgDef(string Name, ArgType Type, bool Required, string Description, int Min = 0, int Max = int.MaxValue, string? Pattern = null, int MaxItems = 0, IReadOnlyList<string>? Keys = null);
