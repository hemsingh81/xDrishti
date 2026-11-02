namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The arithmetic of the fundamental features (P15-05, pure). Series are newest first: <c>eps[0]</c> is the newest quarter <c>q</c>, <c>eps[1]</c> is <c>q-1</c>. Every function answers NaN for an input it cannot use; the caller names the reason. The standard deviation is the
/// sample one (divisor n-1) of the previous eight differences.
/// </summary>
public static class SurpriseMath
{
    public const int SdWindow = 8;

    /// <summary>A deviation at or below this share of the largest earlier difference is rounding noise, not a deviation: the surprise is undefined.</summary>
    public const double RelativeZero = 1e-9;

    /// <summary>Quarters <c>sue_yoy</c> needs: q down to q-12 (the newest difference and the eight before it, each against a quarter four earlier).</summary>
    public const int SueYoyQuarters = 13;

    /// <summary>Quarters <c>sue_ttm</c> needs: q down to q-15.</summary>
    public const int SueTtmQuarters = 16;

    public static double SueYoy(IReadOnlyList<double> eps)
    {
        ArgumentNullException.ThrowIfNull(eps);
        return eps.Count < SueYoyQuarters ? double.NaN : Standardised(Differences(eps));
    }

    public static double SueTtm(IReadOnlyList<double> eps)
    {
        ArgumentNullException.ThrowIfNull(eps);
        if (eps.Count < SueTtmQuarters)
        {
            return double.NaN;
        }

        var ttm = new double[SdWindow + 1 + 4];
        for (var i = 0; i < ttm.Length; i++)
        {
            ttm[i] = eps[i] + eps[i + 1] + eps[i + 2] + eps[i + 3];
        }

        return Standardised(Differences(ttm));
    }

    /// <summary>The sum of four quarters starting at <paramref name="start"/> (newest first); NaN when the series is shorter.</summary>
    public static double Ttm(IReadOnlyList<double> eps, int start)
    {
        ArgumentNullException.ThrowIfNull(eps);
        return start < 0 || eps.Count < start + 4 ? double.NaN : eps[start] + eps[start + 1] + eps[start + 2] + eps[start + 3];
    }

    /// <summary><c>current / previous - 1</c>; NaN when the previous value is 0 or less.</summary>
    public static double Growth(double current, double previous) => previous > 0 ? (current / previous) - 1.0 : double.NaN;

    /// <summary><c>pat / revenue - patPrev / revenuePrev</c> in percentage points; NaN when either revenue is 0 or less.</summary>
    public static double MarginChange(double pat, double revenue, double patPrev, double revenuePrev) =>
        revenue > 0 && revenuePrev > 0 ? 100.0 * ((pat / revenue) - (patPrev / revenuePrev)) : double.NaN;

    /// <summary>The sample standard deviation (n-1); NaN for fewer than two values.</summary>
    public static double SampleSd(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return double.NaN;
        }

        var mean = values.Sum() / values.Count;
        var squares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(squares / (values.Count - 1));
    }

    /// <summary>The rank of <paramref name="current"/> among <paramref name="history"/> in percent: values below it plus half the ties, over the count; NaN when there are fewer than <paramref name="minimum"/> values.</summary>
    public static double Percentile(double current, IReadOnlyList<double> history, int minimum)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0 || history.Count < minimum)
        {
            return double.NaN;
        }

        double below = 0;
        double equal = 0;
        foreach (var value in history)
        {
            if (value < current)
            {
                below++;
            }
            else if (value == current)
            {
                equal++;
            }
        }

        return 100.0 * (below + (0.5 * equal)) / history.Count;
    }

    // d[i] = s[i] - s[i + 4] for i = 0..8
    private static double[] Differences(IReadOnlyList<double> series)
    {
        var diffs = new double[SdWindow + 1];
        for (var i = 0; i < diffs.Length; i++)
        {
            diffs[i] = series[i] - series[i + 4];
        }

        return diffs;
    }

    // the newest difference over the sd of the eight before it; NaN when the sd is 0 or only rounding noise (relative to the size of the differences)
    private static double Standardised(double[] diffs)
    {
        var previous = diffs[1..];
        var sd = SampleSd(previous);
        var scale = previous.Max(Math.Abs);
        return double.IsNaN(sd) || sd <= RelativeZero * scale ? double.NaN : diffs[0] / sd;
    }
}
