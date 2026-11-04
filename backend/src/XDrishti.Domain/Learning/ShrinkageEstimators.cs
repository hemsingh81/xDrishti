namespace XDrishti.Domain.Learning;

/// <summary>The between-cell variance used, and where it came from.</summary>
/// <param name="Value">The tau squared used (never below the floor).</param>
/// <param name="Raw">The DerSimonian-Laird estimate (not below 0); null when it could not be made.</param>
/// <param name="Source"><c>estimated</c> (the estimate reached the floor) or <c>floor</c>.</param>
/// <param name="Siblings">The cells that went in.</param>
/// <param name="Note">Why the floor was used; empty when the estimate was.</param>
public sealed record TauEstimate(double Value, double? Raw, string Source, int Siblings, string Note);

/// <summary>The Beta strength used, and where it came from.</summary>
/// <param name="Strength">The strength used.</param>
/// <param name="Rho">The intra-class correlation estimate; null when none.</param>
/// <param name="Source"><c>estimated</c> or <c>default</c>.</param>
/// <param name="Note">Why the default was used; empty when the estimate was.</param>
public sealed record StrengthEstimate(double Strength, double? Rho, string Source, string Note);

/// <summary>
/// The method-of-moments estimators behind the shrinkage of CF-02 (ADR 0204), across the sibling cells of one parent. Pure and deterministic. Cells that cannot be used (not finite, no trades, no variance) are left out, never turned into a number.
/// </summary>
public static class ShrinkageEstimators
{
    /// <summary>The fewest siblings for an estimate; fewer use the floor and the default strength.</summary>
    public const int MinSiblings = 3;

    /// <summary>
    /// DerSimonian-Laird: <c>w = n / sigma^2</c>, <c>m_w = sum(w m) / sum(w)</c>, <c>Q = sum w (m - m_w)^2</c>, <c>tau^2 = max(0, (Q - (K - 1)) / (sum w - sum w^2 / sum w))</c>. Null for fewer than 2 usable cells or a degenerate denominator.
    /// </summary>
    public static double? DerSimonianLaird(IReadOnlyList<(double Mean, double N, double Sigma2)> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var usable = cells.Where(c => double.IsFinite(c.Mean) && double.IsFinite(c.N) && c.N > 0 && double.IsFinite(c.Sigma2) && c.Sigma2 > 0).ToList();
        if (usable.Count < 2)
        {
            return null;
        }

        var weights = usable.Select(c => c.N / c.Sigma2).ToList();
        var sum = weights.Sum();
        var sumSquares = weights.Sum(w => w * w);
        var weighted = usable.Zip(weights, (c, w) => w * c.Mean).Sum() / sum;
        var q = usable.Zip(weights, (c, w) => w * (c.Mean - weighted) * (c.Mean - weighted)).Sum();
        var denominator = sum - (sumSquares / sum);
        if (!double.IsFinite(q) || !double.IsFinite(denominator) || denominator <= 0)
        {
            return null;
        }

        return Math.Max(0, (q - (usable.Count - 1)) / denominator);
    }

    /// <summary>The tau squared of the siblings, floored. Fewer than <see cref="MinSiblings"/> usable siblings, or no estimate, use the floor and say so.</summary>
    public static TauEstimate TauSquared(IReadOnlyList<(double Mean, double N, double Sigma2)> cells, double floor)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var count = cells.Count(c => double.IsFinite(c.Mean) && double.IsFinite(c.N) && c.N > 0 && double.IsFinite(c.Sigma2) && c.Sigma2 > 0);
        var raw = DerSimonianLaird(cells);
        if (count < MinSiblings)
        {
            return new TauEstimate(floor, raw, "floor", count, $"fewer than {MinSiblings} siblings: tau squared is the floor");
        }

        if (raw is not { } estimate)
        {
            return new TauEstimate(floor, null, "floor", count, "no between-cell estimate: tau squared is the floor");
        }

        return estimate >= floor
            ? new TauEstimate(estimate, estimate, "estimated", count, string.Empty)
            : new TauEstimate(floor, estimate, "floor", count, "the between-cell spread is below the floor: tau squared is the floor");
    }

    /// <summary>The n-weighted mean of every member except <paramref name="excluded"/> (leave-one-out); null when the others carry no weight or a mean is not finite.</summary>
    public static double? LeaveOneOutParent(IReadOnlyList<(double Mean, double N)> members, int excluded)
    {
        ArgumentNullException.ThrowIfNull(members);
        double sum = 0;
        double weight = 0;
        for (var j = 0; j < members.Count; j++)
        {
            if (j == excluded || !(members[j].N > 0))
            {
                continue;
            }

            if (!double.IsFinite(members[j].Mean) || !double.IsFinite(members[j].N))
            {
                return null;
            }

            sum += members[j].N * members[j].Mean;
            weight += members[j].N;
        }

        return weight > 0 ? sum / weight : null;
    }

    /// <summary><c>(1 - rho) / rho</c> clamped to the range; the minimum for <c>rho</c> of 1 or more.</summary>
    public static double StrengthFromRho(double rho, double min, double max) => Math.Clamp((1 - rho) / rho, min, max);

    /// <summary>
    /// The Beta strength from the spread of the siblings' win rates: binomial noise <c>pbar (1 - pbar) / n</c>, DerSimonian-Laird for the rate variance, <c>rho = tau_p^2 / (pbar (1 - pbar))</c>, strength <c>(1 - rho) / rho</c> clamped. With fewer than
    /// <see cref="MinSiblings"/> cells, a pooled rate of 0 or 1, or no spread (rho 0) the <paramref name="fallback"/> is used and the note says so.
    /// </summary>
    public static StrengthEstimate BetaStrength(IReadOnlyList<(double Wins, double N)> cells, double min, double max, double fallback)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var usable = cells.Where(c => double.IsFinite(c.Wins) && double.IsFinite(c.N) && c.N > 0 && c.Wins >= 0 && c.Wins <= c.N).ToList();
        if (usable.Count < MinSiblings)
        {
            return new StrengthEstimate(fallback, null, "default", $"fewer than {MinSiblings} siblings: the default strength");
        }

        var pooled = usable.Sum(c => c.Wins) / usable.Sum(c => c.N);
        var noise = pooled * (1 - pooled);
        if (!(noise > 0))
        {
            return new StrengthEstimate(fallback, null, "default", "no spread in the win rate: the default strength");
        }

        var tau = DerSimonianLaird([.. usable.Select(c => (c.Wins / c.N, c.N, noise))]);
        var rho = tau / noise;
        if (rho is not { } value || !(value > 0))
        {
            return new StrengthEstimate(fallback, null, "default", "the win rates differ no more than chance: the default strength");
        }

        return new StrengthEstimate(StrengthFromRho(value, min, max), value, "estimated", string.Empty);
    }
}
