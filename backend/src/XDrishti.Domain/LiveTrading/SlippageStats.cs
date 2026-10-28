using XDrishti.Domain.Learning;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The statistics of a group of slippage observations (spec P11-02 definitions): mean, sample standard deviation, the 95 % t interval of the mean (table in <see cref="StudentT"/>), median and the order-statistic interval of the median - ranks k and n+1-k with k the
/// largest rank whose binomial tail P(Bin(n, 1/2) &lt;= k-1) is at most 2.5 %. No bootstrap and no percentile above the median. Pure and deterministic.
/// </summary>
public static class SlippageStats
{
    /// <summary>The fewest observations that get any interval.</summary>
    public const int MinimumForInterval = 6;

    private const double Tail = 0.025;

    /// <summary>The summary of at least <see cref="MinimumForInterval"/> finite values; null for fewer or a non-finite value.</summary>
    public static SlippageSummary? Summarise(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var n = values.Count;
        if (n < MinimumForInterval || values.Any(v => !double.IsFinite(v)))
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var mean = sorted.Sum() / n;
        var sd = Math.Sqrt(sorted.Sum(v => (v - mean) * (v - mean)) / (n - 1));
        var half = StudentT.Quantile95(n - 1) * sd / Math.Sqrt(n);
        var median = n % 2 == 1 ? sorted[n / 2] : (sorted[(n / 2) - 1] + sorted[n / 2]) / 2;
        var rank = MedianRank(n);
        return rank is { } k
            ? new SlippageSummary(n, mean, sd, mean - half, mean + half, median, sorted[k - 1], sorted[n - k], 1 - (2 * BinomialCdf(n, k - 1)))
            : new SlippageSummary(n, mean, sd, mean - half, mean + half, median, null, null, null);
    }

    /// <summary>The rank k of the lower end of the median interval (the upper end is n+1-k); null when no rank has a tail of at most 2.5 % (n below 6).</summary>
    public static int? MedianRank(int n)
    {
        int? best = null;
        for (var k = 1; k <= n / 2; k++)
        {
            if (BinomialCdf(n, k - 1) <= Tail)
            {
                best = k;
            }
            else
            {
                break;
            }
        }

        return best;
    }

    /// <summary>P(Bin(n, 1/2) &lt;= m) through the regularised incomplete beta function: I_{1/2}(n - m, m + 1).</summary>
    public static double BinomialCdf(int n, int m) => m < 0 ? 0 : m >= n ? 1 : BetaDistribution.Cdf(0.5, n - m, m + 1);
}
