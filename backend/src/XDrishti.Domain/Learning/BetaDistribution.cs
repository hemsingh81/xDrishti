namespace XDrishti.Domain.Learning;

/// <summary>
/// The Beta distribution functions the cell statistics need (P6-01, ADR 0064): the regularised incomplete beta function (continued fraction, Numerical Recipes 6.4) and its inverse by bisection. Pure, deterministic, no random. Accurate to about 1e-12
/// for shape parameters up to a few thousand, which is far beyond the sizes of a cell.
/// </summary>
public static class BetaDistribution
{
    private const int MaxIterations = 400;

    private const double Epsilon = 1e-14;

    private const double Tiny = 1e-300;

    /// <summary>The cumulative distribution function <c>I_x(a, b)</c>; 0 below 0 and 1 above 1. <paramref name="a"/> and <paramref name="b"/> must be positive.</summary>
    public static double Cdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x))
        {
            throw new ArgumentOutOfRangeException(nameof(a), "The shape parameters are positive.");
        }

        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        var front = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + (a * Math.Log(x)) + (b * Math.Log(1 - x)));
        return x < (a + 1) / (a + b + 2)
            ? front * ContinuedFraction(x, a, b) / a
            : 1 - (front * ContinuedFraction(1 - x, b, a) / b);
    }

    /// <summary>The quantile: the x in [0, 1] with <c>Cdf(x) = p</c>, by bisection (the function is monotone). <paramref name="p"/> is in [0, 1].</summary>
    public static double Quantile(double p, double a, double b)
    {
        if (!(p >= 0 && p <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(p), "The probability is 0 to 1.");
        }

        if (p == 0)
        {
            return 0;
        }

        if (p == 1)
        {
            return 1;
        }

        double lo = 0;
        double hi = 1;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            if (Cdf(mid, a, b) < p)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) / 2;
    }

    private static double Guard(double value) => Math.Abs(value) < Tiny ? Tiny : value;

    private static double ContinuedFraction(double x, double a, double b)
    {
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 / Guard(1 - (qab * x / qap));
        var h = d;
        for (var m = 1; m <= MaxIterations; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 / Guard(1 + (aa * d));
            c = Guard(1 + (aa / c));
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 / Guard(1 + (aa * d));
            c = Guard(1 + (aa / c));
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < Epsilon)
            {
                break;
            }
        }

        return h;
    }

    /// <summary>Natural log of the gamma function (Lanczos, g = 7, n = 9).</summary>
    private static double LogGamma(double x)
    {
        double[] g =
        [
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
            12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        ];
        if (x < 0.5)
        {
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        }

        x -= 1;
        var sum = g[0];
        for (var i = 1; i < 9; i++)
        {
            sum += g[i] / (x + i);
        }

        var t = x + 7.5;
        return (0.5 * Math.Log(2 * Math.PI)) + ((x + 0.5) * Math.Log(t)) - t + Math.Log(sum);
    }
}
