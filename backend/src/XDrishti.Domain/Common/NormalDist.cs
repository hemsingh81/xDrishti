namespace XDrishti.Domain.Common;

/// <summary>
/// The standard normal distribution. <see cref="Cdf"/> is accurate to an absolute error of 1e-14 or better on [-38, 38] and is exactly 0 or 1 outside it.
/// Source (no <c>erf</c> exists in .NET): for |x| &lt; 3 the series of G. Marsaglia, "Evaluating the Normal Distribution", Journal of Statistical Software 11(5), 2004,
/// <c>Phi(x) = 1/2 + phi(x) (x + x^3/3 + x^5/(3*5) + ...)</c> (all terms add, no cancellation); for |x| &gt;= 3 the Laplace continued fraction of the Mills ratio,
/// <c>Q(x) = phi(x) / (x + 1/(x + 2/(x + 3/(x + ...))))</c> (the Mills-ratio fraction of Abramowitz and Stegun, section 26.2), evaluated bottom-up with a fixed depth so the result is deterministic.
/// </summary>
public static class NormalDist
{
    private const double InvSqrt2Pi = 0.3989422804014327;
    private const double Limit = 38.0;
    private const int FractionDepth = 120;

    public static double Pdf(double x) => InvSqrt2Pi * Math.Exp(-0.5 * x * x);

    public static double Cdf(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x <= -Limit)
        {
            return 0.0;
        }

        if (x >= Limit)
        {
            return 1.0;
        }

        var ax = Math.Abs(x);
        if (ax < 3.0)
        {
            var term = ax;
            var sum = ax;
            var x2 = ax * ax;
            for (var i = 3; i < 400; i += 2)
            {
                term *= x2 / i;
                var next = sum + term;
                if (next == sum)
                {
                    break;
                }

                sum = next;
            }

            var half = Pdf(ax) * sum;
            return x >= 0 ? 0.5 + half : 0.5 - half;
        }

        var tail = Tail(ax);
        return x >= 0 ? 1.0 - tail : tail;
    }

    /// <summary>The upper tail <c>1 - Phi(x)</c> for x at least 3.</summary>
    private static double Tail(double x)
    {
        var f = x;
        for (var k = FractionDepth; k >= 1; k--)
        {
            f = x + (k / f);
        }

        return Pdf(x) / f;
    }
}
