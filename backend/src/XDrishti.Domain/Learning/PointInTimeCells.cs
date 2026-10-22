namespace XDrishti.Domain.Learning;

/// <summary>
/// The cell-statistics features of a trade as they stood the day before its decision (P6-04, ADR 0066): for each decision date the pure <see cref="CellStatsCalculator"/> is run with <c>as_of = date - 1 day</c>, so only trades that exited
/// strictly before the decision date are read. The trade itself, and every trade decided on or after that date, cannot be in its own features.
/// </summary>
public static class PointInTimeCells
{
    /// <summary>The five values appended to a row: n, posterior win rate, lower credible bound, expectancy R (NaN when unknown), enough (1 or 0). A cell with no trade yet reads n 0 and the prior.</summary>
    public static IReadOnlyDictionary<string, double[]> Compute(IReadOnlyList<CellInput> cells, IReadOnlyList<CellAsk> asks, CellStatsOptions options)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(asks);
        ArgumentNullException.ThrowIfNull(options);
        var result = new Dictionary<string, double[]>(asks.Count, StringComparer.Ordinal);
        foreach (var day in asks.GroupBy(a => a.DecisionDate).OrderBy(g => g.Key))
        {
            var rows = CellStatsCalculator.Compute(cells, day.Key.AddDays(-1), options);
            var all = new Dictionary<string, CellStatsRow>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row.Window == CellWindow.All)
                {
                    all[row.Cell.Key] = row;
                }
            }

            foreach (var ask in day)
            {
                result[ask.TradeKey] = all.TryGetValue(ask.CellKey, out var r)
                    ? [r.N, r.WinRatePosterior, r.WinRateLower, r.ExpectancyR ?? double.NaN, r.Enough ? 1 : 0]
                    : [double.NaN, double.NaN, double.NaN, double.NaN, double.NaN];
            }
        }

        return result;
    }
}
