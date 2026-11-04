namespace XDrishti.Domain.Confidence;

/// <summary>
/// The minimum track-record length of Bailey and Lopez de Prado in per-trade units (CF-01, decision 7): <c>1 + (1 - skew SR + (kurt - 1)/4 SR^2) (z_alpha / (SR - SR*))^2</c> trades with iid trades. Pure; no edge to detect or an unusable input gives null.
/// </summary>
public static class MinTrackRecord
{
    /// <param name="sr">Per-trade Sharpe ratio (mean R over SD of R).</param>
    /// <param name="srStar">The benchmark per-trade Sharpe ratio.</param>
    /// <param name="skew">Skewness (0 for normal).</param>
    /// <param name="kurt">Kurtosis (3 for normal).</param>
    /// <param name="confidence">One-sided confidence, for example 0.95.</param>
    public static int? Trades(double sr, double srStar, double skew, double kurt, double confidence)
    {
        if (!double.IsFinite(sr) || !double.IsFinite(srStar) || !double.IsFinite(skew) || !double.IsFinite(kurt) || !double.IsFinite(confidence) || confidence <= 0 || confidence >= 1 || sr <= srStar)
        {
            return null;
        }

        var factor = 1 - (skew * sr) + ((kurt - 1) / 4 * sr * sr);
        if (factor <= 0)
        {
            return null;
        }

        var value = 1 + (factor * Math.Pow(NormalDistribution.Quantile(confidence) / (sr - srStar), 2));
        return double.IsFinite(value) && value < int.MaxValue ? (int)Math.Ceiling(Math.Round(value, 9)) : null;
    }
}
