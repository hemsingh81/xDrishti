namespace XDrishti.Domain.Reporting;

/// <summary>
/// Ranks baskets without over-reading a lucky window (BK-07 Decision 3). The rank is on the longest relative strength window that most baskets have a value for. A pair of neighbours is separated when the gap of their RS returns is larger than the standard error of the
/// difference, approximated as the standard deviation of the daily difference of the two indexes times the square root of the common days (the tracking error of the pair times sqrt(days / 252)). It is an indicative, pairwise, uncorrected rule and not a significance test. Pure.
/// </summary>
public static class BasketRankBoard
{
    /// <summary>Fewest index returns for a risk-adjusted figure.</summary>
    public const int MinReturns = 20;

    public static BasketRankBoardResult Compute(IReadOnlyList<RankInput> baskets)
    {
        ArgumentNullException.ThrowIfNull(baskets);
        var windows = baskets.SelectMany(b => b.RelativeStrength.Select(r => r.Window)).Distinct().Order().ToList();
        int? window = null;
        var most = 1;
        foreach (var w in windows)
        {
            var have = baskets.Count(b => Value(b, w) is not null);
            if (have >= 2 && have >= most)
            {
                window = w;
                most = have;
            }
        }

        var all = baskets.Select(b => window is { } w && Value(b, w) is { } v ? Metric.Of(v) : Metric.Unavailable("no relative strength return")).ToList();
        var rows = new List<RankedBasket>(baskets.Count);
        for (var i = 0; i < baskets.Count; i++)
        {
            var b = baskets[i];
            var rank = window is null ? Metric.Insufficient("fewer than 2 baskets have a relative strength return") : RelativeStrength.Rank(all[i], all);
            if (window is not null && all[i].Number is null)
            {
                rank = Reason(b, window.Value);
            }

            rows.Add(new RankedBasket(b.Id, b.RelativeStrength, RiskAdjusted(b.Index), rank));
        }

        return new BasketRankBoardResult(window, rows, window is not null && Separated(baskets, window.Value));
    }

    private static double? Value(RankInput b, int window) => b.RelativeStrength.FirstOrDefault(r => r.Window == window)?.Return.Number;

    private static Metric Reason(RankInput b, int window) => b.RelativeStrength.FirstOrDefault(r => r.Window == window)?.Return is { } r && r.Number is null ? r : Metric.Unavailable("no relative strength return");

    private static Metric RiskAdjusted(IndexSeries index)
    {
        if (index.Status.State != MetricState.Value)
        {
            return index.Status;
        }

        var returns = index.Points.Skip(1).Select(p => p.Return).ToArray();
        if (returns.Length < MinReturns)
        {
            return BasketStats.Short(returns.Length, MinReturns);
        }

        var vol = Math.Sqrt(BasketStats.TradingDays) * BasketStats.Stdev(returns);
        return vol > 1e-12 ? Metric.Of((index.Points[^1].Level / index.Points[0].Level - 1.0) / vol) : Metric.NotEvaluable("the index does not move");
    }

    private static bool Separated(IReadOnlyList<RankInput> baskets, int window)
    {
        var ranked = baskets.Select(b => (Basket: b, Value: Value(b, window))).Where(x => x.Value is not null).OrderByDescending(x => x.Value).ToList();
        for (var i = 0; i + 1 < ranked.Count; i++)
        {
            var (a, b) = (ranked[i], ranked[i + 1]);
            if (Noise(a.Basket.Index, b.Basket.Index) is not { } noise || a.Value!.Value - b.Value!.Value <= noise)
            {
                return false;
            }
        }

        return ranked.Count >= 2;
    }

    /// <summary>The standard error of the difference of two indexes over their common days; null when there are too few.</summary>
    private static double? Noise(IndexSeries a, IndexSeries b)
    {
        var byDate = b.Points.Skip(1).ToDictionary(p => p.Date, p => p.Return);
        var diff = new List<double>();
        foreach (var p in a.Points.Skip(1))
        {
            if (byDate.TryGetValue(p.Date, out var other))
            {
                diff.Add(p.Return - other);
            }
        }

        return diff.Count < 3 ? null : BasketStats.Stdev(diff.ToArray()) * Math.Sqrt(diff.Count);
    }
}
