using XDrishti.Strategies.Lint;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The parameters a strategy declares, as the conditions and the fields see them: name to number, the first of a repeated name, matched ordinally. A fault of a declaration (a repeated name, a bad name) is reported once, at
/// the declaration, and the parameter is still offered as written, so that it does not cascade into every item that uses it.
/// </summary>
internal sealed class DeclaredParams(Dictionary<string, double> values)
{
    /// <summary>The map the conditions are compiled with (the compiler copies it).</summary>
    public IReadOnlyDictionary<string, double> Values { get; } = values;

    public bool TryGet(string name, out double value) => values.TryGetValue(name, out value);

    /// <summary>The closest declared name to a typed one (edit distance at most 2), or null.</summary>
    public string? Closest(string typed) => Suggestions.Closest(typed, values.Keys) is [var first, ..] ? first : null;
}
