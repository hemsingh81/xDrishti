using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>The normal-normal posterior of one cell's mean R (CF-02, ADR 0204).</summary>
/// <param name="Mean">The shrunk mean, <c>parent + B (m - parent)</c>.</param>
/// <param name="Lower">The lower bound: the <c>level</c> quantile of the posterior, mean minus z times the posterior sd (ignores the uncertainty in tau squared, the parent mean and sigma squared).</param>
/// <param name="Blend">B, the share of the way from the parent to the cell's own mean, 0 to 1.</param>
/// <param name="Variance">The posterior variance, <c>B sigma^2 / n</c> (tau squared for n = 0).</param>
public readonly record struct NormalPosterior(double Mean, double Lower, double Blend, double Variance);

/// <summary>
/// Empirical-Bayes shrinkage of a cell's mean R toward its parent (CF-02, ADR 0204). The cell's true mean is <c>N(parent, tau^2)</c> and the observed mean <c>N(true, sigma^2 / n)</c>, so the posterior mean is <c>parent + B (m - parent)</c> with
/// <c>B = tau^2 / (tau^2 + sigma^2 / n)</c> and variance <c>B sigma^2 / n</c>. Pure. A beside-not-instead figure: <see cref="BetaShrinkage"/> (the win rate, shared with the report engine) is untouched.
/// </summary>
public static class NormalShrinkage
{
    /// <summary>
    /// The posterior of a cell. With <paramref name="nEff"/> 0 the cell says nothing: the mean is the parent's and the spread is tau. Null (never a number) for a non-finite input, a non-positive sigma squared, a negative n or tau squared,
    /// or a level outside (0, 0.5).
    /// </summary>
    /// <param name="mean">The cell's observed mean R (ignored when <paramref name="nEff"/> is 0).</param>
    /// <param name="sigma2">The variance of one trade's R.</param>
    /// <param name="nEff">The number of independent trades.</param>
    /// <param name="parentMean">The parent's mean R.</param>
    /// <param name="tau2">The between-cell variance.</param>
    /// <param name="level">The lower bound is this quantile (0.05).</param>
    public static NormalPosterior? Posterior(double mean, double sigma2, double nEff, double parentMean, double tau2, double level)
    {
        if (!double.IsFinite(sigma2) || sigma2 <= 0 || !double.IsFinite(nEff) || nEff < 0 || !double.IsFinite(parentMean) || !double.IsFinite(tau2) || tau2 < 0 || !(level > 0 && level < 0.5))
        {
            return null;
        }

        if (nEff > 0 && !double.IsFinite(mean))
        {
            return null;
        }

        var blend = nEff == 0 || tau2 == 0 ? 0 : tau2 / (tau2 + (sigma2 / nEff));
        var shrunk = nEff == 0 ? parentMean : parentMean + (blend * (mean - parentMean));
        var variance = tau2 * sigma2 / ((nEff * tau2) + sigma2);
        var z = -NormalDistribution.Quantile(level);
        var lower = shrunk - (z * Math.Sqrt(variance));
        return double.IsFinite(shrunk) && double.IsFinite(lower) ? new NormalPosterior(shrunk, lower, blend, variance) : null;
    }
}
