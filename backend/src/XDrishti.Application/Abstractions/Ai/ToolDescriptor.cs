namespace XDrishti.Application.Abstractions.Ai;

/// <summary>One tool the tool server lists: its name, description and the JSON text of its input schema (what the model is told).</summary>
public sealed record ToolDescriptor(string Name, string Description, string InputSchemaJson);
