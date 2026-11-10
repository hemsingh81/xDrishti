namespace XDrishti.Domain.Baskets.Derived;

/// <summary>Chooses members from a best-first ranking: top-k with a buffer for incumbents and a minimum dwell for slow baskets.</summary>
public static class RankSelector
{
    /// <summary>
    /// <paramref name="ranked"/> holds the symbols that pass the gate, best first (rank 1 = index 0). Incumbents keep their place while their rank is within
    /// floor(buffer x k), best k of them; an incumbent outside the buffer that has been in fewer than <paramref name="minDwell"/> sessions also stays; free slots
    /// are filled in rank order from the others. Never more than k. <paramref name="incumbents"/> maps symbol to sessions held at this evaluation.
    /// </summary>
    public static IReadOnlyList<string> Select(IReadOnlyList<string> ranked, int k, double bufferFactor, IReadOnlyDictionary<string, int>? incumbents, int minDwell)
    {
        var limit = (int)Math.Floor((bufferFactor * k) + 1e-9);
        var kept = new List<string>();
        for (var i = 0; i < ranked.Count && kept.Count < k; i++)
        {
            if (incumbents is not null && incumbents.TryGetValue(ranked[i], out var held) && (i + 1 <= limit || held < minDwell))
            {
                kept.Add(ranked[i]);
            }
        }

        for (var i = 0; i < ranked.Count && kept.Count < k; i++)
        {
            if (!kept.Contains(ranked[i]))
            {
                kept.Add(ranked[i]);
            }
        }

        var position = ranked.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);
        return [.. kept.OrderBy(s => position[s])];
    }

    /// <summary>The k of a quantile form: the top fraction of the passing instruments, at least 1 when any pass.</summary>
    public static int QuantileK(int count, double fraction) => count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(count * fraction));
}
