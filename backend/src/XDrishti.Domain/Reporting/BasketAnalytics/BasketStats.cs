namespace XDrishti.Domain.Reporting;

/// <summary>Small numeric helpers shared by the basket kernels (BK-06): sample statistics with n-1, interpolated quantiles, and the cleaned series.</summary>
internal static class BasketStats
{
    public const double TradingDays = 252;

    public static double Mean(ReadOnlySpan<double> x)
    {
        double s = 0;
        foreach (var v in x)
        {
            s += v;
        }

        return x.Length == 0 ? double.NaN : s / x.Length;
    }

    public static double Variance(ReadOnlySpan<double> x)
    {
        if (x.Length < 2)
        {
            return double.NaN;
        }

        var m = Mean(x);
        double s = 0;
        foreach (var v in x)
        {
            s += (v - m) * (v - m);
        }

        return s / (x.Length - 1);
    }

    public static double Stdev(ReadOnlySpan<double> x) => Math.Sqrt(Variance(x));

    public static double Covariance(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        var mx = Mean(x);
        var my = Mean(y);
        double s = 0;
        for (var i = 0; i < x.Length; i++)
        {
            s += (x[i] - mx) * (y[i] - my);
        }

        return s / (x.Length - 1);
    }

    /// <summary>Linear interpolation at position p (0..1) of n-1; the input is sorted by the caller.</summary>
    public static double Quantile(double[] sorted, double p)
    {
        var pos = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + ((pos - lo) * (sorted[hi] - sorted[lo]));
    }

    public static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return Quantile(copy, 0.5);
    }

    public static Metric Short(int n, int min) => Metric.Insufficient($"n = {n}, at least {min} needed");

    /// <summary>
    /// The closes of one instrument in date order. A close that is not a positive finite number is dropped and counted; a date with two different closes is dropped whole as ambiguous (counted once); two equal closes make one.
    /// </summary>
    public static (DateOnly[] Dates, double[] Closes, int Dropped) Clean(IReadOnlyList<DailyClose> closes, DateOnly from, DateOnly to)
    {
        var kept = new List<DailyClose>(closes.Count);
        var dropped = 0;
        foreach (var c in closes)
        {
            if (c.Date < from || c.Date > to)
            {
                continue;
            }

            if (!double.IsFinite(c.Close) || c.Close <= 0)
            {
                dropped++;
                continue;
            }

            kept.Add(c);
        }

        kept.Sort((a, b) => a.Date != b.Date ? a.Date.CompareTo(b.Date) : a.Close.CompareTo(b.Close));
        var dates = new List<DateOnly>(kept.Count);
        var values = new List<double>(kept.Count);
        var i = 0;
        while (i < kept.Count)
        {
            var j = i;
            while (j + 1 < kept.Count && kept[j + 1].Date == kept[i].Date)
            {
                j++;
            }

            if (kept[i].Close == kept[j].Close)
            {
                dates.Add(kept[i].Date);
                values.Add(kept[i].Close);
            }
            else
            {
                dropped++;
            }

            i = j + 1;
        }

        return ([.. dates], [.. values], dropped);
    }
}
