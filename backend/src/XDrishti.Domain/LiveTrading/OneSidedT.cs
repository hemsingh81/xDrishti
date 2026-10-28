namespace XDrishti.Domain.LiveTrading;

/// <summary>The one-sided 95 % Student t quantile of the spec's table (P11-04 G3): df 9 1.833, 14 1.761, 19 1.729, 24 1.711, 29 1.699, 39 1.685, 59 1.671, larger 1.645. A df between table rows takes the larger quantile (the wider bound).</summary>
public static class OneSidedT
{
    private static readonly (int Df, double T)[] Table = [(5, 2.015), (6, 1.943), (7, 1.895), (8, 1.860), (9, 1.833), (14, 1.761), (19, 1.729), (24, 1.711), (29, 1.699), (39, 1.685), (59, 1.671)];

    public static double Quantile95(int df)
    {
        if (df < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(df), "At least 6 observations are needed.");
        }

        foreach (var (rowDf, t) in Table)
        {
            if (df <= rowDf)
            {
                return t;
            }
        }

        return 1.645;
    }

    /// <summary>Mean, sample standard deviation and the one-sided 95 % lower and upper bounds of the mean; null under 6 values or with a value that is not finite.</summary>
    public static (double Mean, double Lower, double Upper)? Bounds(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var n = values.Count;
        if (n < 6 || values.Any(v => !double.IsFinite(v)))
        {
            return null;
        }

        var mean = values.Sum() / n;
        var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (n - 1));
        var half = Quantile95(n - 1) * sd / Math.Sqrt(n);
        return (mean, mean - half, mean + half);
    }
}
