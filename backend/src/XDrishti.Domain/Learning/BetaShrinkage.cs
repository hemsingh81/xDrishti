namespace XDrishti.Domain.Learning;

/// <summary>
/// The Beta posterior of a win rate and its equal-tailed credible interval (P6-01, ADR 0064), in one place so the cell statistics and the report engine (P8-05, ADR 0099) shrink in exactly the same way. Pure.
/// The posterior is <c>Beta(s p + W, s (1 - p) + N - W)</c> for the prior rate <c>p</c>, strength <c>s</c>, (weighted) wins <c>W</c> and (weighted) trades <c>N</c>.
/// </summary>
internal static class BetaShrinkage
{
    /// <summary>The posterior mean and the lower and upper bounds of the credible interval of the given level.</summary>
    public static (double Posterior, double Lower, double Upper) Posterior(double wins, double total, double priorRate, double strength, double credibleLevel)
    {
        var alpha = (strength * priorRate) + wins;
        var beta = (strength * (1 - priorRate)) + (total - wins);
        if (alpha <= 0 || beta <= 0)
        {
            // strength 0 and no weighted wins or losses: the interval is the degenerate point or the full range
            var point = alpha + beta > 0 ? alpha / (alpha + beta) : priorRate;
            return (point, alpha <= 0 ? 0 : point, beta <= 0 ? 1 : point);
        }

        var tail = (1 - credibleLevel) / 2;
        return (alpha / (alpha + beta), BetaDistribution.Quantile(tail, alpha, beta), BetaDistribution.Quantile(1 - tail, alpha, beta));
    }
}
