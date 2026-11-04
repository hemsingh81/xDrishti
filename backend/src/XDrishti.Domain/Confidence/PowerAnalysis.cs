using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The one-sample power statements of CF-01 (decision 6): how many effective trades an edge needs to be seen, and the smallest edge a number of effective trades could see. Normal R is assumed, so the required number is a lower bound. Pure; an input that is
/// not usable gives null (never a number).
/// </summary>
public static class PowerAnalysis
{
    /// <summary>The standard deviation used: the sample one, not below the floor (the SD of a few trades is unreliable).</summary>
    public static double Sigma(double sampleSd, double floor) => double.IsFinite(sampleSd) ? Math.Max(sampleSd, floor) : floor;

    /// <summary><c>ceil(((z_alpha + z_power) sigma / delta)^2)</c> effective trades; null for a non-positive or non-finite sigma or edge, or an alpha or power outside (0, 1).</summary>
    public static int? TradesNeeded(double sigma, double delta, double alpha, double power)
    {
        if (!Usable(sigma, alpha, power) || !double.IsFinite(delta) || delta <= 0)
        {
            return null;
        }

        var n = Math.Pow(Z(alpha, power) * sigma / delta, 2);
        return double.IsFinite(n) && n < int.MaxValue ? (int)Math.Ceiling(Math.Round(n, 9)) : null;
    }

    /// <summary><c>(z_alpha + z_power) sigma / sqrt(nEff)</c>, the smallest edge in R that <paramref name="nEff"/> effective trades could see; null for unusable input.</summary>
    public static double? MinDetectableEdge(double sigma, double nEff, double alpha, double power)
    {
        if (!Usable(sigma, alpha, power) || !double.IsFinite(nEff) || nEff <= 0)
        {
            return null;
        }

        return Z(alpha, power) * sigma / Math.Sqrt(nEff);
    }

    /// <summary>Years to collect <paramref name="needed"/> effective trades at the rate <paramref name="nEff"/> arrived over <paramref name="spanYears"/>; null for no rate or no span.</summary>
    public static double? YearsToEvidence(double nEff, double spanYears, int needed)
    {
        if (!double.IsFinite(nEff) || !double.IsFinite(spanYears) || nEff <= 0 || spanYears <= 0 || needed < 1)
        {
            return null;
        }

        return needed / (nEff / spanYears);
    }

    /// <summary>The words for <see cref="YearsToEvidence"/>: "about 76 years at this rate (if the rate and the edge hold)", or the unavailable text.</summary>
    public static string YearsText(double? years) =>
        years is { } y ? string.Create(CultureInfo.InvariantCulture, $"about {Math.Round(y):0} years at this rate (if the rate and the edge hold)") : "unavailable: no arrival rate";

    private static double Z(double alpha, double power) => NormalDistribution.Quantile(1 - alpha) + NormalDistribution.Quantile(power);

    private static bool Usable(double sigma, double alpha, double power) => double.IsFinite(sigma) && sigma > 0 && double.IsFinite(alpha) && alpha > 0 && alpha < 1 && double.IsFinite(power) && power > 0 && power < 1;
}
