using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The deflated Sharpe ratio of Bailey and Lopez de Prado (CF-03, ADR 0205): the probability that a Sharpe ratio beats the best that luck alone produces among <c>N</c> trials. Everything is in per-trade units (mean R over the standard deviation of R,
/// no annualisation) and <c>T</c> is the effective number of trades. <c>SR0 = sigma_SR [ (1 - g) Phi^-1(1 - 1/N) + g Phi^-1(1 - 1/(N e)) ]</c>; <c>DSR = Phi( (SR - SR0) sqrt(T - 1) / sqrt(1 - skew SR + (kurt - 1)/4 SR^2) )</c>. Pure.
/// N counts neighbouring variants as independent, so the bar is higher than strictly needed (conservative); the formulas rest on normal-theory approximations.
/// </summary>
public static class DeflatedSharpe
{
    /// <summary>The Euler-Mascheroni constant <c>g</c>.</summary>
    public const double EulerMascheroni = 0.5772156649015329;

    /// <summary>Fewer effective trades than this give no probability.</summary>
    public const int MinTrades = 20;

    /// <summary>The expected maximum Sharpe ratio of <paramref name="trials"/> trials with no edge whose Sharpe ratios have standard deviation <paramref name="sigmaSr"/>; 0 for one trial; NaN for a negative or non-finite spread or no trial.</summary>
    public static double ExpectedMax(int trials, double sigmaSr)
    {
        if (trials < 1 || !double.IsFinite(sigmaSr) || sigmaSr < 0)
        {
            return double.NaN;
        }

        if (trials == 1)
        {
            return 0;
        }

        var n = (double)trials;
        return sigmaSr * (((1 - EulerMascheroni) * NormalDistribution.Quantile(1 - (1 / n))) + (EulerMascheroni * NormalDistribution.Quantile(1 - (1 / (n * Math.E)))));
    }

    /// <summary>The probabilistic Sharpe ratio: the probability that the true Sharpe ratio is above <paramref name="benchmark"/> given <paramref name="sr"/> over <paramref name="effectiveTrades"/>.</summary>
    /// <param name="sr">The Sharpe ratio per trade.</param>
    /// <param name="benchmark">The Sharpe ratio to beat.</param>
    /// <param name="effectiveTrades">T, at least <see cref="MinTrades"/>.</param>
    /// <param name="skew">Skewness of R.</param>
    /// <param name="kurt">Kurtosis of R (3 for normal).</param>
    public static SharpeProbability Probabilistic(double sr, double benchmark, double effectiveTrades, double skew, double kurt)
    {
        if (!double.IsFinite(sr))
        {
            return SharpeProbability.Missing("the Sharpe ratio is not a number");
        }

        if (!double.IsFinite(benchmark))
        {
            return SharpeProbability.Missing("the luck bar is not a number");
        }

        if (!double.IsFinite(effectiveTrades) || effectiveTrades < MinTrades)
        {
            return SharpeProbability.Missing(string.Create(CultureInfo.InvariantCulture, $"fewer than {MinTrades} effective trades"));
        }

        if (!double.IsFinite(skew) || !double.IsFinite(kurt))
        {
            return SharpeProbability.Missing("skew or kurtosis is not a number");
        }

        var variance = 1 - (skew * sr) + ((kurt - 1) / 4 * sr * sr);
        if (!(variance > 0))
        {
            return SharpeProbability.Missing("the skew and kurtosis term is not positive");
        }

        return SharpeProbability.Of(NormalDistribution.Cdf((sr - benchmark) * Math.Sqrt(effectiveTrades - 1) / Math.Sqrt(variance)));
    }

    /// <summary>The deflated Sharpe ratio: the probabilistic Sharpe ratio against <see cref="ExpectedMax"/>. One trial degenerates to the probabilistic Sharpe ratio against 0.</summary>
    public static SharpeProbability Deflated(double sr, int trials, double sigmaSr, double effectiveTrades, double skew, double kurt)
    {
        if (trials < 1)
        {
            return SharpeProbability.Missing("no trial");
        }

        if (trials > 1 && (!double.IsFinite(sigmaSr) || sigmaSr < 0))
        {
            return SharpeProbability.Missing("the spread of the trial Sharpe ratios is not usable");
        }

        return Probabilistic(sr, ExpectedMax(trials, trials == 1 ? 0 : sigmaSr), effectiveTrades, skew, kurt);
    }
}
