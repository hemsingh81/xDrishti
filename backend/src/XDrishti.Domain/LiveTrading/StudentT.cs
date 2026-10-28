namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The two-sided 95 % Student-t quantile from a constant table (spec P11-02): the row of the nearest lower degrees of freedom is used, which is the slightly wider interval. df 5 to 8 are added below the spec's rows because the smallest sample that gets
/// a mean interval is <c>Live:Estimates:MinN</c> = 6 (df 5). 60 to 119 use 2.000 and 120 and above 1.96.
/// </summary>
internal static class StudentT
{
    private static readonly (int Df, double T)[] Rows =
    [
        (5, 2.571), (6, 2.447), (7, 2.365), (8, 2.306), (9, 2.262), (14, 2.145), (19, 2.093), (29, 2.045), (39, 2.023), (59, 2.001), (60, 2.000), (120, 1.96),
    ];

    /// <summary>The quantile for the degrees of freedom; df below 5 is not supported (a sample under 6 gets no interval).</summary>
    public static double Quantile95(int df)
    {
        if (df < Rows[0].Df)
        {
            throw new ArgumentOutOfRangeException(nameof(df), "A mean interval needs at least 6 observations.");
        }

        var t = Rows[0].T;
        foreach (var (rowDf, value) in Rows)
        {
            if (rowDf <= df)
            {
                t = value;
            }
        }

        return t;
    }
}
