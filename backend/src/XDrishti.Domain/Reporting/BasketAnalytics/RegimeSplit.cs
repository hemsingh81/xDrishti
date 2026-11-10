using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The basket index by the market regime of the previous session (BK-06 m16): per regime the days, the mean daily return, the annualised volatility and the share of days the basket beat the index. A regime with too few days is shown as insufficient, never dropped;
/// a day whose previous session has no regime code is counted as unknown and sits in no group. Pure.
/// </summary>
public static class RegimeSplit
{
    public static RegimeResult Compute(IndexSeries index, IReadOnlyList<DailyReturn> benchmark, IReadOnlyDictionary<DateOnly, int?> regimes, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(regimes);
        ArgumentNullException.ThrowIfNull(options);
        if (index.Status.State != MetricState.Value)
        {
            return new RegimeResult(index.Status, [], 0);
        }

        if (regimes.Count == 0)
        {
            return new RegimeResult(Metric.Unavailable("no regime code is recorded for the window"), [], 0);
        }

        var bench = new Dictionary<DateOnly, double>(benchmark.Count);
        foreach (var r in benchmark)
        {
            bench[r.Date] = r.Value;
        }

        var groups = new SortedDictionary<int, List<(double Basket, double? Benchmark)>>();
        var unknown = 0;
        for (var i = 1; i < index.Points.Count; i++)
        {
            var p = index.Points[i];
            if (!regimes.TryGetValue(p.PreviousSession, out var code) || code is null)
            {
                unknown++;
                continue;
            }

            if (!groups.TryGetValue(code.Value, out var list))
            {
                groups[code.Value] = list = [];
            }

            list.Add((p.Return, bench.TryGetValue(p.Date, out var rm) ? rm : null));
        }

        var rows = new List<RegimeRow>(groups.Count);
        foreach (var (code, days) in groups)
        {
            if (days.Count < options.MinDaysPerRegime)
            {
                var few = BasketStats.Short(days.Count, options.MinDaysPerRegime);
                rows.Add(new RegimeRow(code, days.Count, few, few, few));
                continue;
            }

            var returns = days.Select(d => d.Basket).ToArray();
            var paired = days.Where(d => d.Benchmark is not null).ToList();
            rows.Add(new RegimeRow(
                code,
                days.Count,
                Metric.Of(BasketStats.Mean(returns)),
                Metric.Of(Math.Sqrt(BasketStats.TradingDays) * BasketStats.Stdev(returns)),
                paired.Count == 0 ? Metric.Unavailable("the benchmark has no return on these days") : Metric.Of((double)paired.Count(d => d.Basket > d.Benchmark) / paired.Count)));
        }

        return new RegimeResult(rows.Count == 0 ? Metric.Empty("no day has a regime code") : Metric.Of(rows.Count), rows, unknown);
    }
}
