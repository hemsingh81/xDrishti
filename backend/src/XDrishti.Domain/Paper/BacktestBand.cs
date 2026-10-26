using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The composition-matched bootstrap of spec P10-04 (ADR 0122). For each draw the i-th paper slot takes one R, with replacement, from the backtest trades of the cell of the i-th paper trade, so the draw has the paper set's cells in the paper set's
/// proportions. The band is the 5th to 95th percentile of the draw means, the drawdown reference the 90th percentile of the draws' largest falls (slot order), both with the nearest-rank percentile of <see cref="MonteCarlo"/> and its
/// platform-independent <see cref="SeededRandom"/>. Pure: the same inputs give the same numbers. Picks are made in cell order so a permutation of the paper trades leaves the band unchanged and moves only the paper's own fall.
/// </summary>
public static class BacktestBand
{
    /// <summary>The most points on the fan; above this many trades every k-th trade and the last one are shown.</summary>
    public const int MaxFanPoints = 120;

    private const double Low = 0.05;
    private const double High = 0.95;

    /// <summary>The seed of the band's generator: the first 8 bytes of the sha256 of <c>epoch id | snapshot key | n</c>.</summary>
    public static ulong SeedOf(long epochId, string snapshotKey, int n)
    {
        ArgumentNullException.ThrowIfNull(snapshotKey);
        return SeededRandom.SeedOf(string.Create(CultureInfo.InvariantCulture, $"{epochId}|{snapshotKey}|{n}"));
    }

    /// <summary>The seed of the paper mean's own interval: the same key with a fixed suffix.</summary>
    public static ulong IntervalSeedOf(long epochId, string snapshotKey, int n)
    {
        ArgumentNullException.ThrowIfNull(snapshotKey);
        return SeededRandom.SeedOf(string.Create(CultureInfo.InvariantCulture, $"{epochId}|{snapshotKey}|{n}|interval"));
    }

    /// <summary>
    /// The band, or null when it cannot be drawn: no paper trade, an invalid draw count or a paper cell without a backtest trade. The caller states why before it gets here.
    /// </summary>
    public static BandResult? Compute(IReadOnlyList<PaperSlot> paper, IReadOnlyList<CellReference> references, BandOptions options, ulong seed, ulong intervalSeed)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(options);
        if (paper.Count == 0 || !options.IsValid)
        {
            return null;
        }

        var byCell = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            byCell[reference.CellKey] = reference.Results;
        }

        var n = paper.Count;
        var pools = new IReadOnlyList<double>[n];
        for (var i = 0; i < n; i++)
        {
            if (!byCell.TryGetValue(paper[i].CellKey, out var pool) || pool.Count == 0)
            {
                return null;
            }

            pools[i] = pool;
        }

        var order = Enumerable.Range(0, n).OrderBy(i => paper[i].CellKey, StringComparer.Ordinal).ToArray();
        var fanAt = FanIndexes(n);
        var draws = options.Draws;
        var means = new double[draws];
        var falls = new double[draws];
        var columns = fanAt.Select(_ => new double[draws]).ToArray();
        var picks = new double[n];
        var rng = new SeededRandom(seed);
        for (var d = 0; d < draws; d++)
        {
            double sum = 0;
            foreach (var slot in order)
            {
                var pool = pools[slot];
                picks[slot] = pool[rng.Next(pool.Count)];
                sum += picks[slot];
            }

            means[d] = sum / n;
            double cumulative = 0, peak = 0, worst = 0;
            var next = 0;
            for (var i = 0; i < n; i++)
            {
                cumulative += picks[i];
                peak = Math.Max(peak, cumulative);
                worst = Math.Max(worst, peak - cumulative);
                if (next < fanAt.Length && fanAt[next] == i + 1)
                {
                    columns[next][d] = cumulative;
                    next++;
                }
            }

            falls[d] = worst;
        }

        Array.Sort(means);
        Array.Sort(falls);
        var results = paper.Select(s => s.R).ToList();
        var paperMean = PaperMean(results);
        var (intervalLow, intervalHigh) = PaperInterval(results, draws, intervalSeed);
        var p05 = MonteCarlo.Percentile(means, Low);
        var p95 = MonteCarlo.Percentile(means, High);
        var paperFall = MonteCarlo.MaxDrawdown(results);
        var p90Fall = MonteCarlo.Percentile(falls, 0.90);
        return new BandResult(
            n, draws, seed, paperMean, intervalLow, intervalHigh, p05, MonteCarlo.Percentile(means, 0.50), p95, RankOf(means, paperMean),
            paperMean < p05 ? "below" : paperMean > p95 ? "above" : "within",
            paperFall, MonteCarlo.Percentile(falls, 0.50), p90Fall, MonteCarlo.Percentile(falls, High),
            paperFall <= p90Fall ? "within" : "above",
            BuildFan(fanAt, columns, results));
    }

    /// <summary>The trade counts the fan shows: every trade up to <see cref="MaxFanPoints"/>, else every k-th trade and the last.</summary>
    internal static int[] FanIndexes(int n)
    {
        if (n <= MaxFanPoints)
        {
            return [.. Enumerable.Range(1, n)];
        }

        var stride = (n + MaxFanPoints - 1) / MaxFanPoints;
        var indexes = new List<int>();
        for (var i = stride; i < n; i += stride)
        {
            indexes.Add(i);
        }

        indexes.Add(n);
        return [.. indexes];
    }

    private static FanPoint[] BuildFan(int[] fanAt, double[][] columns, List<double> results)
    {
        var fan = new FanPoint[fanAt.Length];
        var cumulative = new double[results.Count + 1];
        for (var i = 0; i < results.Count; i++)
        {
            cumulative[i + 1] = cumulative[i] + results[i];
        }

        for (var f = 0; f < fanAt.Length; f++)
        {
            Array.Sort(columns[f]);
            fan[f] = new FanPoint(fanAt[f], MonteCarlo.Percentile(columns[f], Low), MonteCarlo.Percentile(columns[f], 0.50), MonteCarlo.Percentile(columns[f], High), cumulative[fanAt[f]]);
            columns[f] = [];
        }

        return fan;
    }

    /// <summary>The paper mean, summed in ascending order so a permutation of the trades gives the same number.</summary>
    public static double PaperMean(IReadOnlyList<double> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.Order().Sum() / results.Count;
    }

    /// <summary>The 90% percentile-bootstrap interval of the paper mean itself (5th and 95th percentile of the resampled means), seeded; the trades are taken in ascending order so a permutation changes nothing.</summary>
    public static (double Low, double High) PaperInterval(IReadOnlyList<double> results, int draws, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(results);
        var sortedResults = results.Order().ToArray();
        var rng = new SeededRandom(seed);
        var means = new double[draws];
        for (var d = 0; d < draws; d++)
        {
            double sum = 0;
            for (var i = 0; i < sortedResults.Length; i++)
            {
                sum += sortedResults[rng.Next(sortedResults.Length)];
            }

            means[d] = sum / sortedResults.Length;
        }

        Array.Sort(means);
        return (MonteCarlo.Percentile(means, Low), MonteCarlo.Percentile(means, High));
    }

    private static double RankOf(double[] sortedMeans, double value)
    {
        double below = 0, equal = 0;
        foreach (var m in sortedMeans)
        {
            if (m < value)
            {
                below++;
            }
            else if (m.Equals(value))
            {
                equal++;
            }
        }

        return (below + (equal / 2)) / sortedMeans.Length * 100;
    }
}
