using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Reporting;

/// <summary>Cross-sectional dispersion of member returns on a day (BK-06 m10): the stdev, its mean over the last days and the interquartile range. Needs the minimum voters on every day it uses. Pure.</summary>
public static class Dispersion
{
    public static DispersionResult Compute(IReadOnlyDictionary<string, IReadOnlyList<DailyReturn>> returns, DateOnly asOf, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(options);
        var byDate = new SortedDictionary<DateOnly, List<double>>();
        foreach (var symbol in returns.Keys.Order(StringComparer.Ordinal))
        {
            foreach (var r in returns[symbol])
            {
                if (r.Date <= asOf && double.IsFinite(r.Value))
                {
                    if (!byDate.TryGetValue(r.Date, out var list))
                    {
                        byDate[r.Date] = list = [];
                    }

                    list.Add(r.Value);
                }
            }
        }

        if (!byDate.TryGetValue(asOf, out var today))
        {
            return new DispersionResult(Metric.Unavailable("no member has a return on the day"), Metric.Unavailable("no member has a return on the day"), Metric.Unavailable("no member has a return on the day"), 0);
        }

        if (today.Count < options.MinVoters)
        {
            var shortFigure = BasketStats.Short(today.Count, options.MinVoters);
            return new DispersionResult(shortFigure, shortFigure, shortFigure, today.Count);
        }

        var sorted = today.Order().ToArray();
        var iqr = BasketStats.Quantile(sorted, 0.75) - BasketStats.Quantile(sorted, 0.25);
        var recent = byDate.Where(kv => kv.Key <= asOf).TakeLast(options.AveragingDays).ToList();
        var mean = recent.Count < options.AveragingDays || recent.Any(kv => kv.Value.Count < options.MinVoters)
            ? Metric.Insufficient($"{recent.Count(kv => kv.Value.Count >= options.MinVoters)} days with at least {options.MinVoters} members, {options.AveragingDays} needed")
            : Metric.Of(recent.Average(kv => BasketStats.Stdev(kv.Value.ToArray())));
        return new DispersionResult(Metric.Of(BasketStats.Stdev(today.ToArray())), mean, Metric.Of(iqr), today.Count);
    }
}
