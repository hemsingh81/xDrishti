namespace XDrishti.Domain.Paper;

/// <summary>One difference between two configurations.</summary>
/// <param name="Key">The key.</param>
/// <param name="Class">frozen, risk_down or free.</param>
/// <param name="Before">The earlier value; null when the key was added.</param>
/// <param name="After">The later value; null when the key was removed.</param>
public sealed record ConfigChange(string Key, ConfigClass Class, string? Before, string? After);

/// <summary>The differences between two configurations, classed (spec P10-05). Pure.</summary>
public static class ConfigDiff
{
    /// <summary>Every added, removed or changed key, in ordinal key order.</summary>
    public static IReadOnlyList<ConfigChange> Compare(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new List<ConfigChange>();
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var had = before.TryGetValue(key, out var b);
            var has = after.TryGetValue(key, out var a);
            if (had && has && string.Equals(a, b, StringComparison.Ordinal))
            {
                continue;
            }

            var from = had ? b : null;
            var to = has ? a : null;
            changes.Add(new ConfigChange(key, PaperConfigKeys.Classify(key, from, to), from, to));
        }

        return changes;
    }
}
