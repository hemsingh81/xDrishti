namespace XDrishti.Domain.Lab;

/// <summary>
/// A seeded randomisation test of the mean R (spec P5-05, assumption 9): every trade keeps its size and its sign is a coin flip. The p-value is <c>(draws with mean at least the observed + 1) / (draws + 1)</c>. It is not a re-run with random entries.
/// </summary>
public static class SignFlipBaseline
{
    /// <param name="results">R of each trade.</param>
    /// <param name="draws">Number of random sign sets, 1 or more.</param>
    /// <param name="seed">The seed of the generator.</param>
    /// <returns>The result, or null when there are fewer than <see cref="MonteCarlo.MinTrades"/> trades.</returns>
    public static SignFlipResult? Run(IReadOnlyList<double> results, int draws, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfLessThan(draws, 1);
        if (results.Count < MonteCarlo.MinTrades)
        {
            return null;
        }

        var observed = results.Average();
        var rng = new SeededRandom(seed);
        var means = new double[draws];
        var reached = 0;
        for (var d = 0; d < draws; d++)
        {
            double sum = 0;
            for (var i = 0; i < results.Count; i++)
            {
                var size = Math.Abs(results[i]);
                sum += (rng.NextBits() & 1) == 0 ? size : -size;
            }

            means[d] = sum / results.Count;
            if (means[d] >= observed)
            {
                reached++;
            }
        }

        Array.Sort(means);
        return new SignFlipResult(results.Count, draws, seed, observed, MonteCarlo.Percentile(means, 0.95), (reached + 1.0) / (draws + 1.0));
    }
}
