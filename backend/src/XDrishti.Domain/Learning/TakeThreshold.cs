namespace XDrishti.Domain.Learning;

/// <summary>
/// The threshold search of the take policy (ADR 0075), shared by <see cref="MetaPolicyEvaluator"/> and the feature-set comparison (P13-05): the lowest grid value (0, step, 2 x step ... below 1) that maximises the summed R of the candidates scoring at least the threshold,
/// while keeping at least <c>minKeptShare</c> of them. Ties keep the lower threshold. Sequential sums, so the same rows in the same order give the same answer.
/// </summary>
internal static class TakeThreshold
{
    /// <summary>The best threshold and the share of the rows it keeps. The caller guarantees at least one row.</summary>
    public static (double Threshold, double KeptShare) Choose(IReadOnlyList<(double Score, double R)> rows, double step, double minKeptShare)
    {
        var best = 0.0;
        var bestValue = double.NegativeInfinity;
        var bestShare = 1.0;
        for (var k = 0; k * step < 1; k++)
        {
            var t = Math.Round(k * step, 10);
            var kept = 0;
            var sum = 0.0;
            foreach (var (score, r) in rows)
            {
                if (score >= t)
                {
                    kept++;
                    sum += r;
                }
            }

            var share = kept / (double)rows.Count;
            if (share < minKeptShare)
            {
                continue;
            }

            if (sum > bestValue)
            {
                best = t;
                bestValue = sum;
                bestShare = share;
            }
        }

        return (best, bestShare);
    }
}
