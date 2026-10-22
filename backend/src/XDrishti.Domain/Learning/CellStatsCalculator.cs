namespace XDrishti.Domain.Learning;

/// <summary>
/// The cell statistics of doc 09 layer 1 (P6-01, ADR 0064). Pure: no clock (the as-of date is an argument), no I/O, no random. For each cell and window it reads the trades that exited on or before the as-of date (never a later one: the point-in-time rule),
/// weights them by recency, shrinks the win rate toward the parent group with a Beta prior and reports the credible interval.
/// <para>
/// Weight of a trade: <c>0.5 ^ (days from its exit to the as-of date / half-life)</c>. The prior: the system's weighted win rate <c>r_sys</c> (all cells in the window; 0.5 if no weight); the strategy's rate <c>r_str = (W_str + s r_sys) / (N_str + s)</c> where
/// <c>W</c> are weighted wins, <c>N</c> the sum of weights and <c>s</c> the strength; the cell's posterior is <c>Beta(s r_str + W, s (1 - r_str) + N - W)</c>. The parent groups contain the cell's own trades (stated limit).
/// </para>
/// </summary>
public static class CellStatsCalculator
{
    private static readonly CellWindow[] Windows = [CellWindow.All, CellWindow.Days250, CellWindow.Days60];

    /// <summary>The rows, for every cell and every window, in the order of the input then the windows. The options must be usable (<see cref="CellStatsOptions.Problems"/>).</summary>
    public static IReadOnlyList<CellStatsRow> Compute(IReadOnlyList<CellInput> cells, DateOnly asOf, CellStatsOptions options)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Problems().Count > 0)
        {
            throw new ArgumentException("The options are not usable.", nameof(options));
        }

        var slices = new Dictionary<CellWindow, List<(CellInput Input, List<CellTrade> Trades)>>();
        var strategyRates = new Dictionary<CellWindow, Dictionary<string, double>>();
        foreach (var window in Windows)
        {
            var list = cells.Select(c => (Input: c, Trades: InWindow(c.Trades, asOf, window))).ToList();
            var (sysWins, sysWeight) = Sum(list.SelectMany(s => s.Trades), asOf, options);
            var systemRate = sysWeight > 0 ? sysWins / sysWeight : 0.5;
            slices[window] = list;
            strategyRates[window] = list.GroupBy(s => s.Input.Cell.Strategy, StringComparer.Ordinal).ToDictionary(
                g => g.Key,
                g =>
                {
                    var (wins, weight) = Sum(g.SelectMany(s => s.Trades), asOf, options);
                    var denominator = weight + options.PriorStrength;
                    return denominator > 0 ? (wins + (options.PriorStrength * systemRate)) / denominator : systemRate;
                },
                StringComparer.Ordinal);
        }

        var rows = new List<CellStatsRow>(cells.Count * Windows.Length);
        for (var i = 0; i < cells.Count; i++)
        {
            foreach (var window in Windows)
            {
                var (input, trades) = slices[window][i];
                rows.Add(Row(input, window, trades, asOf, strategyRates[window][input.Cell.Strategy], options));
            }
        }

        return rows.AsReadOnly();
    }

    private static List<CellTrade> InWindow(IReadOnlyList<CellTrade> trades, DateOnly asOf, CellWindow window)
    {
        var days = window.Days();
        return [.. trades.Where(t => t.ExitDate <= asOf && (days is null || t.ExitDate.DayNumber > asOf.DayNumber - days.Value))
            .OrderBy(t => t.ExitDate)];
    }

    private static double Weight(CellTrade trade, DateOnly asOf, CellStatsOptions options) => Math.Pow(0.5, (asOf.DayNumber - trade.ExitDate.DayNumber) / options.RecencyHalfLifeDays);

    private static (double Wins, double Weight) Sum(IEnumerable<CellTrade> trades, DateOnly asOf, CellStatsOptions options)
    {
        double wins = 0;
        double weight = 0;
        foreach (var t in trades)
        {
            var w = Weight(t, asOf, options);
            weight += w;
            if (t.R > 0)
            {
                wins += w;
            }
        }

        return (wins, weight);
    }

    private static CellStatsRow Row(CellInput input, CellWindow window, List<CellTrade> trades, DateOnly asOf, double priorRate, CellStatsOptions options)
    {
        var (wWins, wTotal) = Sum(trades, asOf, options);
        var s = options.PriorStrength;
        var alpha = (s * priorRate) + wWins;
        var beta = (s * (1 - priorRate)) + (wTotal - wWins);
        double posterior;
        double lower;
        double upper;
        if (alpha <= 0 || beta <= 0)
        {
            // strength 0 and no weighted wins or losses: the interval is the degenerate point or the full range
            posterior = alpha + beta > 0 ? alpha / (alpha + beta) : priorRate;
            lower = alpha <= 0 ? 0 : posterior;
            upper = beta <= 0 ? 1 : posterior;
        }
        else
        {
            posterior = alpha / (alpha + beta);
            var tail = (1 - options.CredibleLevel) / 2;
            lower = BetaDistribution.Quantile(tail, alpha, beta);
            upper = BetaDistribution.Quantile(1 - tail, alpha, beta);
        }

        var wins = trades.Count(t => t.R > 0);
        double winSum = 0;
        double winWeight = 0;
        double lossSum = 0;
        double lossWeight = 0;
        double rSum = 0;
        foreach (var t in trades)
        {
            var w = Weight(t, asOf, options);
            rSum += w * t.R;
            if (t.R > 0)
            {
                winSum += w * t.R;
                winWeight += w;
            }
            else if (t.R < 0)
            {
                lossSum += w * t.R;
                lossWeight += w;
            }
        }

        var any = trades.Count > 0;
        var mae = trades.Select(t => t.MaeR).Order().ToList();
        var mfe = trades.Select(t => t.MfeR).Order().ToList();
        return new CellStatsRow(
            input.Cell,
            window,
            asOf,
            input.Costed,
            trades.Count,
            wins,
            wTotal,
            any ? (double)wins / trades.Count : null,
            priorRate,
            posterior,
            lower,
            upper,
            winWeight > 0 ? winSum / winWeight : null,
            lossWeight > 0 ? lossSum / lossWeight : null,
            any ? rSum / wTotal : null,
            lossSum < 0 ? winSum / -lossSum : null,
            MaxDrawdown(trades),
            Quantile(mae, 0.5),
            Quantile(mae, 0.9),
            Quantile(mfe, 0.5),
            Quantile(mfe, 0.9),
            trades.Count >= options.MinTrades);
    }

    private static double MaxDrawdown(List<CellTrade> ordered)
    {
        double running = 0;
        double peak = 0;
        double worst = 0;
        foreach (var t in ordered)
        {
            running += t.R;
            peak = Math.Max(peak, running);
            worst = Math.Max(worst, peak - running);
        }

        return worst;
    }

    /// <summary>Quantile by linear interpolation between the closest ranks (type 7) of a sorted list; null for an empty one.</summary>
    internal static double? Quantile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var position = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(position);
        var hi = Math.Min(lo + 1, sorted.Count - 1);
        return sorted[lo] + ((position - lo) * (sorted[hi] - sorted[lo]));
    }
}
