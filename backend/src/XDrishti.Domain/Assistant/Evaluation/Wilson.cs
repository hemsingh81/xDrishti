namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The Wilson score interval of a rate (spec P9-09 D3): <c>k</c> successes in <c>n</c> trials, <c>z</c> 1.96 for 95 %. With no trial the interval is the whole range, which says nothing. Pure.</summary>
public static class Wilson
{
    public const double Z95 = 1.96;

    /// <summary>The lower and upper bound of the interval.</summary>
    public static (double Low, double High) Interval(int k, int n, double z = Z95)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(k, n);
        if (n == 0)
        {
            return (0, 1);
        }

        var p = (double)k / n;
        var z2 = z * z;
        var denominator = 1 + (z2 / n);
        var centre = (p + (z2 / (2 * n))) / denominator;
        var margin = z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4.0 * n * n))) / denominator;
        return (Math.Max(0, centre - margin), Math.Min(1, centre + margin));
    }
}
