namespace XDrishti.Domain.Lab;

/// <summary>
/// Seeded bootstrap of a list of trade results in R (spec P5-05, assumption 6): each draw takes as many trades as there are, with replacement, in the order drawn, and records the mean and the largest peak-to-trough fall of the cumulative R. Pure.
/// The trades are treated as independent, so clustered losses are understated; the report says so.
/// </summary>
public static class MonteCarlo
{
    /// <summary>The fewest trades a bootstrap is run on.</summary>
    public const int MinTrades = 2;

    /// <param name="results">R of each trade in the order of exit.</param>
    /// <param name="draws">Number of resamples, 1 or more.</param>
    /// <param name="seed">The seed of the generator.</param>
    /// <returns>The result, or null when there are fewer than <see cref="MinTrades"/> trades.</returns>
    public static MonteCarloResult? Run(IReadOnlyList<double> results, int draws, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfLessThan(draws, 1);
        if (results.Count < MinTrades)
        {
            return null;
        }

        var rng = new SeededRandom(seed);
        var means = new double[draws];
        var drawdowns = new double[draws];
        for (var d = 0; d < draws; d++)
        {
            double sum = 0, peak = 0, worst = 0;
            for (var i = 0; i < results.Count; i++)
            {
                sum += results[rng.Next(results.Count)];
                peak = Math.Max(peak, sum);
                worst = Math.Max(worst, peak - sum);
            }

            means[d] = sum / results.Count;
            drawdowns[d] = worst;
        }

        Array.Sort(means);
        Array.Sort(drawdowns);
        return new MonteCarloResult(
            results.Count,
            draws,
            seed,
            results.Average(),
            MaxDrawdown(results),
            Percentile(means, 0.05),
            Percentile(means, 0.50),
            Percentile(means, 0.95),
            Percentile(drawdowns, 0.50),
            Percentile(drawdowns, 0.95),
            (double)means.Count(m => m <= 0) / draws);
    }

    /// <summary>The largest peak-to-trough fall of the cumulative sum, starting from 0.</summary>
    public static double MaxDrawdown(IReadOnlyList<double> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        double sum = 0, peak = 0, worst = 0;
        foreach (var r in results)
        {
            sum += r;
            peak = Math.Max(peak, sum);
            worst = Math.Max(worst, peak - sum);
        }

        return worst;
    }

    /// <summary>The value at rank <c>round(p x (n - 1))</c> of a sorted array.</summary>
    internal static double Percentile(double[] sorted, double p) => sorted[(int)Math.Round(p * (sorted.Length - 1), MidpointRounding.AwayFromZero)];
}
