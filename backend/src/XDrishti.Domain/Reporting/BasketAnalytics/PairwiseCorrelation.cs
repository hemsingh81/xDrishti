using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// Average pairwise correlation and effective number of bets (BK-06 m11, m12). The pair rule is the Pearson of <see cref="CorrelationTable"/> (P7-06), so the gate, the fill and the profile cannot disagree on a number. Plain Pearson, no shrinkage (Decision 3);
/// N_eff = 1 + (N - 1)(1 - rho_bar) is an equal-correlation heuristic, not a measured count of bets (Decision 4). Pure.
/// </summary>
public static class PairwiseCorrelation
{
    /// <summary>Correlation of two return series of equal length; null when either has no variance; rounding past 1 is clamped.</summary>
    public static double? Pair(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        return CorrelationTable.Pearson(x, y) is { } rho ? Math.Clamp(rho, -1.0, 1.0) : null;
    }

    /// <summary>Builds the pair correlations over the last sessions and summarises them. More than the maximum members: the most liquid by <paramref name="adv"/> are kept and the result says so.</summary>
    public static CorrelationResult Compute(IReadOnlyDictionary<string, IReadOnlyList<DailyReturn>> returns, IReadOnlyDictionary<string, double> adv, DateOnly asOf, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(adv);
        ArgumentNullException.ThrowIfNull(options);
        var symbols = returns.Keys.Order(StringComparer.Ordinal).ToList();
        var sampled = symbols.Count > options.MaxCorrelationMembers;
        if (sampled)
        {
            symbols = [.. symbols.OrderByDescending(s => adv.TryGetValue(s, out var a) && double.IsFinite(a) ? a : 0.0).ThenBy(s => s, StringComparer.Ordinal).Take(options.MaxCorrelationMembers).Order(StringComparer.Ordinal)];
        }

        var dates = symbols.SelectMany(s => returns[s]).Where(r => r.Date <= asOf).Select(r => r.Date).Distinct().OrderDescending().Take(options.CorrelationWindow).ToList();
        var slot = new Dictionary<DateOnly, int>(dates.Count);
        for (var i = 0; i < dates.Count; i++)
        {
            slot[dates[i]] = i;
        }

        var grid = new double[symbols.Count][];
        for (var s = 0; s < symbols.Count; s++)
        {
            grid[s] = new double[dates.Count];
            Array.Fill(grid[s], double.NaN);
            foreach (var r in returns[symbols[s]])
            {
                if (slot.TryGetValue(r.Date, out var at) && double.IsFinite(r.Value))
                {
                    grid[s][at] = double.IsNaN(grid[s][at]) ? r.Value : double.NaN;
                }
            }
        }

        var rhos = new List<double>();
        var x = new double[dates.Count];
        var y = new double[dates.Count];
        for (var a = 0; a < symbols.Count; a++)
        {
            for (var b = a + 1; b < symbols.Count; b++)
            {
                var n = 0;
                for (var d = 0; d < dates.Count; d++)
                {
                    if (!double.IsNaN(grid[a][d]) && !double.IsNaN(grid[b][d]))
                    {
                        x[n] = grid[a][d];
                        y[n] = grid[b][d];
                        n++;
                    }
                }

                if (n >= Math.Max(2, options.MinCommonSessions) && Pair(x[..n], y[..n]) is { } rho)
                {
                    rhos.Add(rho);
                }
            }
        }

        return Summarise(symbols.Count, rhos, options, sampled);
    }

    /// <summary>The mean of the pair correlations and the effective number of bets (clamped to 1..N).</summary>
    public static CorrelationResult Summarise(int members, IReadOnlyList<double> pairs, BasketProfileOptions options, bool sampled)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(options);
        if (members < options.MinCorrelationMembers)
        {
            var few = BasketStats.Short(members, options.MinCorrelationMembers);
            return new CorrelationResult(few, few, members, pairs.Count, sampled);
        }

        if (pairs.Count < options.MinCorrelationPairs)
        {
            var thin = Metric.Insufficient($"{pairs.Count} pairs with enough common sessions, at least {options.MinCorrelationPairs} needed");
            return new CorrelationResult(thin, thin, members, pairs.Count, sampled);
        }

        var rho = BasketStats.Mean(pairs.ToArray());
        var effective = Math.Clamp(1.0 + ((members - 1) * (1.0 - rho)), 1.0, members);
        return new CorrelationResult(Metric.Of(rho), Metric.Of(effective), members, pairs.Count, sampled);
    }
}
