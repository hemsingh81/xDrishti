namespace XDrishti.Strategies.Lint;

/// <summary>
/// "Did you mean": the names of a vocabulary closest to what was typed. The distance is the optimal string alignment distance (insert, delete or replace one letter, or swap two neighbours: each costs 1), compared
/// without regard to case, so <c>RSI</c> is at distance 0 from <c>rsi</c> and <c>1d</c> from <c>1D</c> and a wrong case is the first thing suggested. Only the closest tier is returned (everything at the smallest
/// distance found), and only if that distance is within the limit.
/// </summary>
internal static class Suggestions
{
    /// <summary>The largest distance at which a name is still suggested (the spec: edit distance 2).</summary>
    public const int MaxDistance = 2;

    /// <summary>How many names a message lists at most.</summary>
    public const int MaxListed = 5;

    /// <summary>The optimal string alignment distance between two texts; by default letters are compared after invariant lower-casing, so a different case costs nothing.</summary>
    public static int Distance(string first, string second, bool ignoreCase = true)
    {
        var a = ignoreCase ? first.ToLowerInvariant() : first;
        var b = ignoreCase ? second.ToLowerInvariant() : second;
        var previousPrevious = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var best = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    best = Math.Min(best, previousPrevious[j - 2] + 1);
                }

                current[j] = best;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// The names of the vocabulary at the smallest distance from <paramref name="typed"/> (case ignored), nearest tier only, at most <see cref="MaxListed"/>; empty when none is within
    /// <paramref name="maxDistance"/>. Within the tier the names that differ least when the case counts come first (<c>2m</c> suggests <c>1m</c> before <c>1M</c>), then ordinal order. A text of one or two letters
    /// is only matched within distance 1: at distance 2 everything of three letters is "close" to it and the suggestion would be noise.
    /// </summary>
    public static IReadOnlyList<string> Closest(string typed, IEnumerable<string> vocabulary, int maxDistance = MaxDistance)
    {
        maxDistance = Math.Min(maxDistance, Math.Max(1, typed.Length - 1));
        var best = int.MaxValue;
        var found = new List<string>();
        foreach (var name in vocabulary.Distinct(StringComparer.Ordinal))
        {
            var distance = Distance(typed, name);
            if (distance > maxDistance || distance > best)
            {
                continue;
            }

            if (distance < best)
            {
                best = distance;
                found.Clear();
            }

            found.Add(name);
        }

        var ordered = found.OrderBy(n => Distance(typed, n, false)).ThenBy(n => n, StringComparer.Ordinal);
        return [.. ordered.Take(MaxListed)];
    }

    /// <summary>The names as a sentence part: <c>`a`</c>, <c>`a` or `b`</c>, <c>`a`, `b` or `c`</c>.</summary>
    public static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => "`" + names[0] + "`",
        _ => string.Join(", ", names.Take(names.Count - 1).Select(n => "`" + n + "`")) + " or `" + names[^1] + "`",
    };
}
