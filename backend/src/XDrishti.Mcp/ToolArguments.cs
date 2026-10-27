namespace XDrishti.Mcp;

/// <summary>The checked arguments of a call: every value has the declared type and is within its bounds.</summary>
internal sealed class ToolArguments(IReadOnlyDictionary<string, object> values)
{
    public string? String(string name) => values.TryGetValue(name, out var v) ? (string)v : null;

    public int? Integer(string name) => values.TryGetValue(name, out var v) ? (int)v : null;

    public bool? Boolean(string name) => values.TryGetValue(name, out var v) ? (bool)v : null;

    public IReadOnlyDictionary<string, string> Map(string name) => values.TryGetValue(name, out var v) ? (IReadOnlyDictionary<string, string>)v : new Dictionary<string, string>();
}
