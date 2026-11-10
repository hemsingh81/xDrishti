namespace XDrishti.Domain.Reporting;

/// <summary>Relative strength of the basket index against the benchmark (BK-06 m8): the line RS(d) = I(d) / I_m(d) rebased to 100, its returns over fixed session windows, and the rank among baskets. Pure.</summary>
public static class RelativeStrength
{
    public static RelativeStrengthResult Compute(IndexSeries index, IReadOnlyList<DailyClose> benchmark, IReadOnlyList<int> windows)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(windows);
        if (index.Status.State != MetricState.Value)
        {
            return new RelativeStrengthResult([], [.. windows.Select(w => new RelativeStrengthReturn(w, index.Status))]);
        }

        var (dates, closes, _) = BasketStats.Clean(benchmark, DateOnly.MinValue, DateOnly.MaxValue);
        var bench = new Dictionary<DateOnly, double>(dates.Length);
        for (var i = 0; i < dates.Length; i++)
        {
            bench[dates[i]] = closes[i];
        }

        var line = new List<RelativeStrengthPoint>(index.Points.Count);
        double baseRatio = 0;
        foreach (var p in index.Points)
        {
            if (!bench.TryGetValue(p.Date, out var m))
            {
                continue;
            }

            var ratio = p.Level / m;
            baseRatio = line.Count == 0 ? ratio : baseRatio;
            line.Add(new RelativeStrengthPoint(p.Date, 100.0 * ratio / baseRatio));
        }

        var returns = new List<RelativeStrengthReturn>(windows.Count);
        foreach (var w in windows)
        {
            returns.Add(new RelativeStrengthReturn(w, w < 1 || line.Count < w + 1 ? BasketStats.Short(line.Count, w + 1) : Metric.Of((line[^1].Value / line[^(w + 1)].Value) - 1.0)));
        }

        return new RelativeStrengthResult(line, returns);
    }

    /// <summary>The rank of <paramref name="mine"/> among the baskets passed in (1 = strongest, ties share the lower rank); at least two baskets must have a value.</summary>
    public static Metric Rank(Metric mine, IReadOnlyList<Metric> all)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(all);
        var values = all.Where(x => x.Number is not null).Select(x => x.Number!.Value).ToList();
        if (mine.Number is not { } own)
        {
            return Metric.Unavailable("this basket has no return for the window");
        }

        return values.Count < 2 ? Metric.Insufficient($"{values.Count} baskets with a value, at least 2 needed") : Metric.Of(1 + values.Count(v => v > own));
    }
}
