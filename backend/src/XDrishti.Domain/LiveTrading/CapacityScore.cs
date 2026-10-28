using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The capacity score (spec P11-02): the share of orders filled in full, as the mean of a Beta posterior with an equal-tailed 95 % credible interval. The account level uses the uniform prior Beta(1 + k, 1 + n - k); a bucket shrinks toward its parent's rate
/// with strength s: Beta(s p + k, s (1 - p) + n - k), the same shrinkage as the cells (<see cref="BetaShrinkage"/>). Fewer than <c>MinN</c> counted orders is <c>insufficient</c> and no score (nor the shrunk value) is given. Display only: selection does not read it.
/// </summary>
public static class CapacityScore
{
    private const double CredibleLevel = 0.95;

    /// <summary>The account-level score (uniform prior).</summary>
    public static CapacityResult ForAccount(CapacityCounts counts, int minN) => Compute(counts, 0.5, 2, minN, 2);

    /// <summary>The score of a bucket (or any child scope), shrunk toward the parent's rate with the given prior strength. A parent rate that is not a number is refused as unavailable.</summary>
    public static CapacityResult ForChild(CapacityCounts counts, double? parentRate, int priorStrength, int minN)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Counted == 0 || counts.Counted < minN)
        {
            return Compute(counts, 0.5, 1, minN, priorStrength); // empty or insufficient needs no parent rate
        }

        return parentRate is { } p && p is >= 0 and <= 1
            ? Compute(counts, p, priorStrength, minN, priorStrength)
            : new CapacityResult(Metric.Unavailable("the parent rate is not available"), null, null, priorStrength);
    }

    private static CapacityResult Compute(CapacityCounts counts, double prior, int strength, int minN, int reportedStrength)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var n = counts.Counted;
        if (n == 0)
        {
            return new CapacityResult(Metric.Empty("no counted order"), null, null, reportedStrength);
        }

        if (n < minN)
        {
            return new CapacityResult(Metric.Insufficient($"n = {n}, at least {minN} needed"), null, null, reportedStrength);
        }

        var (mean, lo, hi) = BetaShrinkage.Posterior(counts.Full, n, prior, strength, CredibleLevel);
        return new CapacityResult(Metric.Of(mean), lo, hi, reportedStrength);
    }
}
