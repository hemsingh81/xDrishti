namespace XDrishti.Domain.Assistant;

/// <summary>The indices kept when a series is cut for the model (spec BK-12 AC 7, Decision 3) and the step between them.</summary>
public sealed record SeriesSample(IReadOnlyList<int> Indices, int Step, bool Sampled);

/// <summary>
/// Down-samples the aligned series of a chart to at most <c>max</c> points for the model (spec BK-12 AC 7): the first and last point and the lowest and highest point of every series are always kept, so a drawdown is not smoothed away; the rest are evenly spaced. Pure and
/// deterministic: the same input gives the same indices. The screen keeps the full series.
/// </summary>
public static class SeriesSampler
{
    public const int MaxPoints = 120;

    public static SeriesSample Sample(int count, int max, params IReadOnlyList<double?>[] series)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 2);
        if (count <= max)
        {
            return new SeriesSample([.. Enumerable.Range(0, count)], 1, false);
        }

        var keep = new SortedSet<int> { 0, count - 1 };
        foreach (var s in series)
        {
            int? low = null;
            int? high = null;
            for (var i = 0; i < Math.Min(count, s.Count); i++)
            {
                if (s[i] is not { } v)
                {
                    continue;
                }

                low = low is { } l && s[l]!.Value <= v ? low : i;
                high = high is { } h && s[h]!.Value >= v ? high : i;
            }

            if (low is { } lo)
            {
                keep.Add(lo);
            }

            if (high is { } hi)
            {
                keep.Add(hi);
            }
        }

        // the extremes are at most 2 + 2 per series; the evenly spaced points fill what is left, so the union never passes max
        var room = Math.Max(1, max - keep.Count);
        var step = (int)Math.Ceiling(count / (double)room);
        for (var i = 0; i < count; i += step)
        {
            keep.Add(i);
        }

        return new SeriesSample([.. keep], step, true);
    }
}
