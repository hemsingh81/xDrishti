namespace XDrishti.SimOracle;

/// <summary>
/// The comparison arithmetic of spec P4-05 (D5, D6, D7), written from the text of the spec and from nothing of the engine. A table is built from the closed trades of each candidate: the <i>common set</i> is the keys present under every
/// candidate; each figure is taken on a candidate's own trades and on the common set; the paired figure is the mean over the common entries of a candidate's result minus the first candidate's. The figures are those of the spec: win rate over trades,
/// mean, median (the mean of the two middle values for an even count), the downside deviation <c>sqrt(mean(min(R, 0)^2))</c>, total, profit factor, average hold; the diagnostics count a <i>winner</i> as a trade whose exact result is above 0.
/// The code is deliberately plain: loops and one sort, no library statistics.
/// </summary>
public static class OracleComparison
{
    /// <summary>The table of the candidates, in the order given.</summary>
    /// <param name="candidates">One or more candidates.</param>
    public static IReadOnlyList<OracleComparisonRow> Table(IReadOnlyList<OracleComparisonCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var common = new HashSet<string>(candidates[0].Trades.Select(t => t.Key), StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var keys = new HashSet<string>(candidate.Trades.Select(t => t.Key), StringComparer.Ordinal);
            common.RemoveWhere(k => !keys.Contains(k));
        }

        var first = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var trade in candidates[0].Trades)
        {
            first[trade.Key] = trade.R;
        }

        var rows = new List<OracleComparisonRow>();
        foreach (var candidate in candidates)
        {
            var shared = candidate.Trades.Where(t => common.Contains(t.Key)).ToList();
            var sum = 0.0;
            foreach (var trade in shared)
            {
                sum += trade.R - first[trade.Key];
            }

            rows.Add(Row(candidate, Sample(candidate.Trades), Sample(shared), shared.Count == 0 ? double.NaN : sum / shared.Count));
        }

        return rows;
    }

    private static OracleComparisonRow Row(OracleComparisonCandidate candidate, OracleSample own, OracleSample common, double paired)
    {
        var trades = candidate.Trades;
        var winners = trades.Where(t => t.RExact > 0).ToList();
        var runners = trades.Where(t => t.Mfe >= 1.0).ToList();
        return new OracleComparisonRow(
            candidate.Name,
            own,
            common,
            paired,
            Share(winners.Count(t => t.Mae > 0.8), winners.Count),
            Share(winners.Count(t => t.Mae > 1.2), winners.Count),
            MedianOf(trades.Select(t => t.Mfe).ToList()),
            Share(trades.Count(t => t.Mfe >= 1.0), trades.Count),
            Share(trades.Count(t => t.Mfe >= 2.0), trades.Count),
            Share(trades.Count(t => t.Mfe >= 3.0), trades.Count),
            MedianOf(runners.Select(t => t.Mfe - t.RExact).ToList()),
            Share(runners.Count(t => t.Mfe - t.RExact > t.Mfe / 2), runners.Count));
    }

    private static double Share(int part, int whole) => whole == 0 ? double.NaN : (double)part / whole;

    private static OracleSample Sample(IReadOnlyList<OracleComparisonTrade> trades)
    {
        var reasons = new Dictionary<OracleExitReason, int>();
        double total = 0, gains = 0, losses = 0, squares = 0, held = 0;
        var wins = 0;
        foreach (var trade in trades)
        {
            total += trade.R;
            held += trade.HeldMinutes;
            if (trade.R > 0)
            {
                wins++;
                gains += trade.R;
            }
            else if (trade.R < 0)
            {
                losses += -trade.R;
                squares += trade.R * trade.R;
            }

            reasons[trade.Reason] = reasons.GetValueOrDefault(trade.Reason) + 1;
        }

        var n = trades.Count;
        return n == 0
            ? new OracleSample(0, 0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, reasons)
            : new OracleSample(n, wins, (double)wins / n, total / n, MedianOf(trades.Select(t => t.R).ToList()), Math.Sqrt(squares / n), total, losses > 0 ? gains / losses : double.NaN, held / n, reasons);
    }

    private static double MedianOf(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        values.Sort();
        var n = values.Count;
        return n % 2 == 1 ? values[(n - 1) / 2] : (values[(n / 2) - 1] + values[n / 2]) / 2.0;
    }
}
