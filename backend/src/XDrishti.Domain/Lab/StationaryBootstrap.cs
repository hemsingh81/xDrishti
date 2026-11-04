namespace XDrishti.Domain.Lab;

/// <summary>
/// The stationary block bootstrap of Politis and Romano (1994) over sessions (CF-03, ADR 0205). The unit is the session: all trades with the same entry date, kept together in their order. A block continues to the next session with probability <c>1 - 1/L</c> and restarts at a random
/// session with probability <c>1/L</c>, wrapping around, so same-day dependence stays inside a unit and serial dependence stays inside a block. Pure and seeded: a seed gives the same draws on every run. The iid <see cref="MonteCarlo"/> stays as it is.
/// </summary>
public static class StationaryBootstrap
{
    /// <summary>The fewest sessions a bootstrap is run on.</summary>
    public const int MinSessions = 2;

    /// <summary>Below this many sessions the interval is not trustworthy and says so.</summary>
    public const int TrustworthySessions = 30;

    /// <summary>The block length when none is configured: <c>round(k^(1/3))</c>, at least 2.</summary>
    public static BlockLength AutoBlockLength(int sessions)
    {
        var length = (int)Math.Round(Math.Cbrt(Math.Max(sessions, 0)), MidpointRounding.AwayFromZero);
        return length >= 2 ? new BlockLength(length, null) : new BlockLength(2, "block length set to the minimum of 2 sessions");
    }

    /// <summary>The indices of <paramref name="sessions"/> sessions drawn by the stationary scheme: the first is <c>floor(u k)</c>; each later one restarts with probability <c>1/L</c> (<c>u &lt; 1/L</c>, taking a fresh index from the next uniform) or is the previous plus one, modulo <c>k</c>.</summary>
    /// <param name="sessions">k, 1 or more.</param>
    /// <param name="meanBlock">L, 1 or more (1 is an iid resample of sessions).</param>
    /// <param name="uniform">The stream of uniforms in <c>[0, 1)</c>.</param>
    public static IReadOnlyList<int> Indices(int sessions, int meanBlock, Func<double> uniform)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sessions, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(meanBlock, 1);
        ArgumentNullException.ThrowIfNull(uniform);
        var result = new int[sessions];
        Fill(result, sessions, 1.0 / meanBlock, uniform);
        return result;
    }

    /// <summary>The bootstrap of the trades of <paramref name="sessions"/> (each the R of one session in order) with <paramref name="draws"/> resamples of that many sessions.</summary>
    /// <param name="sessions">Each session's trade results in R, in the order of the trades.</param>
    /// <param name="draws">Number of resamples, 1 or more.</param>
    /// <param name="meanBlock">L, 1 or more.</param>
    /// <param name="seed">The seed of the generator.</param>
    /// <returns>The result, or null when there are fewer than <see cref="MinSessions"/> sessions with a trade.</returns>
    public static BlockBootstrapResult? Run(IReadOnlyList<double[]> sessions, int draws, int meanBlock, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentOutOfRangeException.ThrowIfLessThan(draws, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(meanBlock, 1);
        var k = sessions.Count;
        if (k < MinSessions || sessions.Any(s => s.Length == 0))
        {
            return null;
        }

        var trades = sessions.Sum(s => s.Length);
        var total = sessions.Sum(s => s.Sum());
        var rng = new SeededRandom(seed);
        var picked = new int[k];
        var p = 1.0 / meanBlock;
        var means = new double[draws];
        var drawdowns = new double[draws];
        for (var d = 0; d < draws; d++)
        {
            Fill(picked, k, p, rng.NextDouble);
            double sum = 0, peak = 0, worst = 0;
            var count = 0;
            foreach (var index in picked)
            {
                foreach (var r in sessions[index])
                {
                    sum += r;
                    peak = Math.Max(peak, sum);
                    worst = Math.Max(worst, peak - sum);
                }

                count += sessions[index].Length;
            }

            means[d] = sum / count;
            drawdowns[d] = worst;
        }

        var notPositive = means.Count(m => m <= 0);
        Array.Sort(means);
        Array.Sort(drawdowns);
        return new BlockBootstrapResult(
            k,
            trades,
            meanBlock,
            draws,
            seed,
            total / trades,
            MonteCarlo.Percentile(means, 0.05),
            MonteCarlo.Percentile(means, 0.50),
            MonteCarlo.Percentile(means, 0.95),
            MonteCarlo.Percentile(drawdowns, 0.50),
            MonteCarlo.Percentile(drawdowns, 0.95),
            (double)notPositive / draws);
    }

    private static void Fill(int[] target, int k, double restart, Func<double> uniform)
    {
        target[0] = Fresh(uniform(), k);
        for (var i = 1; i < target.Length; i++)
        {
            target[i] = uniform() < restart ? Fresh(uniform(), k) : (target[i - 1] + 1) % k;
        }
    }

    private static int Fresh(double u, int k) => Math.Min(k - 1, (int)Math.Floor(u * k));
}
