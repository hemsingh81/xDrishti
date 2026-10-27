namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>What the checks need to know about the YAML, read by the existing strategy reader: the text, its plain JSON tree (scalars as strings; null when it does not read), the declared version and id, and the number of params.</summary>
public sealed record DraftYamlFacts(string Text, string? TreeJson, int? Version, string? Id, int ParamCount);
