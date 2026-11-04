namespace XDrishti.Domain.Confidence;

/// <summary>
/// The standard normal distribution (CF-01): the cumulative function <c>Phi</c> and its inverse. Pure and deterministic. <c>Phi</c> is the complementary error function (series below 2.5, continued fraction above; relative error about 1e-13). <see cref="Quantile"/> starts from Acklam's rational approximation (relative error below 1.2e-9) and refines it with one Newton step on that accurate <c>Cdf</c>, so both are good to about 1e-12 away from the extreme tails.
/// </summary>
public static class NormalDistribution
{
    private static readonly double[] A = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
    private static readonly double[] B = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
    private static readonly double[] C = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
    private static readonly double[] D = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];

    private const double Low = 0.02425;

    /// <summary>The cumulative distribution function; NaN in, NaN out.</summary>
    public static double Cdf(double x) => double.IsNaN(x) ? double.NaN : 0.5 * Erfc(-x / Math.Sqrt(2));

    /// <summary>The quantile (inverse of <see cref="Cdf"/>) for <paramref name="p"/> strictly between 0 and 1; the infinities at 0 and 1; an argument outside [0, 1] or NaN throws.</summary>
    public static double Quantile(double p)
    {
        if (double.IsNaN(p) || p < 0 || p > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "The probability is 0 to 1.");
        }

        if (p == 0)
        {
            return double.NegativeInfinity;
        }

        if (p == 1)
        {
            return double.PositiveInfinity;
        }

        double x;
        if (p < Low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((C[0] * q) + C[1]) * q + C[2]) * q + C[3]) * q + C[4]) * q + C[5];
            x /= (((((D[0] * q) + D[1]) * q + D[2]) * q + D[3]) * q) + 1;
        }
        else if (p <= 1 - Low)
        {
            var q = p - 0.5;
            var r = q * q;
            x = (((((A[0] * r) + A[1]) * r + A[2]) * r + A[3]) * r + A[4]) * r + A[5];
            x *= q;
            x /= (((((B[0] * r) + B[1]) * r + B[2]) * r + B[3]) * r + B[4]) * r + 1;
        }
        else
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -((((((C[0] * q) + C[1]) * q + C[2]) * q + C[3]) * q + C[4]) * q + C[5]);
            x /= (((((D[0] * q) + D[1]) * q + D[2]) * q + D[3]) * q) + 1;
        }

        for (var i = 0; i < 1; i++)
        {
            var density = Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);
            if (density > 0)
            {
                x -= (Cdf(x) - p) / density;
            }
        }

        return x;
    }

    // erfc to about 1e-13 relative: the Taylor series of erf below z = 2.5, the continued fraction of erfc above it (Abramowitz and Stegun 7.1.5 and 7.1.14).
    private static double Erfc(double x)
    {
        var z = Math.Abs(x);
        double r;
        if (z < 2.5)
        {
            double term = z;
            double sum = z;
            for (var n = 1; n < 200; n++)
            {
                term *= -z * z / n;
                var add = term / ((2 * n) + 1);
                sum += add;
                if (Math.Abs(add) < 1e-17 * Math.Abs(sum))
                {
                    break;
                }
            }

            r = 1 - (2 / Math.Sqrt(Math.PI) * sum);
        }
        else
        {
            var t = z;
            for (var k = 120; k >= 1; k--)
            {
                t = z + (k / 2.0 / t);
            }

            r = Math.Exp(-z * z) / (Math.Sqrt(Math.PI) * t);
        }

        return x >= 0 ? r : 2 - r;
    }
}
