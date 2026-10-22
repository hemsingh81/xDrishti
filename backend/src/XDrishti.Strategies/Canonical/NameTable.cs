namespace XDrishti.Strategies.Canonical;

/// <summary>The spellings of the members of one enum in the strategy file and in the canonical text, in a fixed order (the order of the enum): one table, read by the file reader, the writer, the schema and the canonical form.</summary>
public sealed class NameTable<T>
    where T : struct, Enum
{
    private readonly (string Name, T Value)[] _entries;

    internal NameTable(params (string Name, T Value)[] entries) => _entries = entries;

    /// <summary>The spellings, in order.</summary>
    public IReadOnlyList<string> Names => [.. _entries.Select(e => e.Name)];

    /// <summary>The spelling of a member.</summary>
    public string Name(T value) => _entries.First(e => EqualityComparer<T>.Default.Equals(e.Value, value)).Name;

    /// <summary>The member of a spelling (exact, case-sensitive), or false.</summary>
    public bool TryParse(string? name, out T value)
    {
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                value = entry.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
