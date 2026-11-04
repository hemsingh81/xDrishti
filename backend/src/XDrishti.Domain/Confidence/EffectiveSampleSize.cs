using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// How many independent trades a set of trades is worth (CF-01, ADR 0203). Two estimates in trade units, the smaller wins (they describe partly the same dependence, so they are not multiplied): the cluster estimate (Kish design effect with the one-way ANOVA
/// intra-cluster correlation, clusters = the India entry session) and the serial estimate (<c>N (1 - r1) / (1 + r1)</c>, <c>r1</c> the lag-1 autocorrelation of the date-ordered daily mean R). Pure and deterministic. The result is never above N and never below 1,
/// and a negative correlation is set to 0: the evidence may shrink, never grow. A figure that cannot be computed is <see cref="EffectiveEstimate.Unavailable"/>, never N and never a pass.
/// </summary>
public static class EffectiveSampleSize
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Kish: <c>DE = 1 + (m - 1) rho</c>, <c>n_eff = n / DE</c> for <paramref name="n"/> trades in clusters of average size <paramref name="averageClusterSize"/> with within-cluster correlation <paramref name="rho"/> (clipped to 0..1).</summary>
    public static EffectiveEstimate Kish(int n, double averageClusterSize, double rho)
    {
        if (n < 1 || !double.IsFinite(averageClusterSize) || averageClusterSize < 1 || !double.IsFinite(rho))
        {
            return EffectiveEstimate.Unavailable("the cluster inputs are not usable");
        }

        var de = 1 + ((averageClusterSize - 1) * Math.Clamp(rho, 0, 1));
        return From(n, n / de, EffectiveEstimate.ClusterMethod, string.Empty);
    }

    /// <summary>
    /// The cluster estimate from the trades (cluster key, R). With one cluster the answer is 1 (everything happened on one day: one observation, the conservative reading). With more than one and fewer than <paramref name="minClusters"/>, or with one trade per
    /// cluster, it is unavailable (the variance components are noise; N is not claimed). Order of the input does not matter.
    /// </summary>
    public static EffectiveEstimate Cluster(IReadOnlyList<(string Key, double R)> trades, int minClusters = 5)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var n = trades.Count;
        if (n < 2 || trades.Any(t => !double.IsFinite(t.R)))
        {
            return EffectiveEstimate.Unavailable(n < 2 ? "fewer than 2 trades" : "a result is not a finite number");
        }

        var groups = trades.GroupBy(t => t.Key, StringComparer.Ordinal).Select(g => g.Select(t => t.R).ToArray()).ToList();
        var k = groups.Count;
        if (k == 1)
        {
            return new EffectiveEstimate(1, n, EffectiveEstimate.ClusterMethod, "all trades are in one entry session: counted as one observation");
        }

        if (k < minClusters)
        {
            return EffectiveEstimate.Unavailable($"{k.ToString(Inv)} entry sessions, fewer than the {minClusters.ToString(Inv)} the cluster estimate needs");
        }

        if (k == n)
        {
            return EffectiveEstimate.Unavailable("one trade per entry session: no within-session information");
        }

        var grand = trades.Sum(t => t.R) / n;
        double ssb = 0;
        double ssw = 0;
        double sumSquares = 0;
        foreach (var g in groups)
        {
            var mean = g.Average();
            ssb += g.Length * (mean - grand) * (mean - grand);
            ssw += g.Sum(r => (r - mean) * (r - mean));
            sumSquares += (double)g.Length * g.Length;
        }

        var msb = ssb / (k - 1);
        var msw = ssw / (n - k);
        var m0 = (n - (sumSquares / n)) / (k - 1);
        var denominator = msb + ((m0 - 1) * msw);
        var rho = denominator > 0 ? (msb - msw) / denominator : 0;   // 0/0 (identical results everywhere) is no correlation
        return Kish(n, (double)n / k, double.IsFinite(rho) ? rho : 0);
    }

    /// <summary>The lag-1 autocorrelation of a series about its mean; 0 for a constant series; null for fewer than 2 values or a non-finite value.</summary>
    public static double? Lag1(IReadOnlyList<double> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count < 2 || series.Any(v => !double.IsFinite(v)))
        {
            return null;
        }

        var mean = series.Average();
        double denominator = 0;
        double numerator = 0;
        for (var i = 0; i < series.Count; i++)
        {
            denominator += (series[i] - mean) * (series[i] - mean);
            if (i > 0)
            {
                numerator += (series[i - 1] - mean) * (series[i] - mean);
            }
        }

        return denominator > 0 ? numerator / denominator : 0;
    }

    /// <summary>The serial factor applied to <paramref name="n"/> trades: <c>n (1 - r1) / (1 + r1)</c>; a non-positive <paramref name="r1"/> gives n; never below 1.</summary>
    public static EffectiveEstimate FromLag1(int n, double r1)
    {
        if (n < 1 || !double.IsFinite(r1))
        {
            return EffectiveEstimate.Unavailable("the serial inputs are not usable");
        }

        var r = Math.Clamp(r1, 0, 0.999999);
        return From(n, n * (1 - r) / (1 + r), EffectiveEstimate.SerialMethod, string.Empty);
    }

    /// <summary>The serial estimate from the date-ordered daily mean R (sessions without a trade left out); unavailable below <paramref name="minSessions"/> values. <paramref name="tradeCount"/> is N in trades (default: the series length).</summary>
    public static EffectiveEstimate Serial(IReadOnlyList<double> dailyR, int minSessions, int? tradeCount = null)
    {
        ArgumentNullException.ThrowIfNull(dailyR);
        if (dailyR.Count < minSessions)
        {
            return EffectiveEstimate.Unavailable($"{dailyR.Count.ToString(Inv)} sessions, fewer than the {minSessions.ToString(Inv)} the serial estimate needs");
        }

        return Lag1(dailyR) is { } r1 ? FromLag1(tradeCount ?? dailyR.Count, r1) : EffectiveEstimate.Unavailable("the daily series is not usable");
    }

    /// <summary>The smaller of the defined estimates (a tie goes to the cluster one); with none, unavailable with both reasons.</summary>
    public static EffectiveEstimate Combine(EffectiveEstimate cluster, EffectiveEstimate serial)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(serial);
        if (cluster.IsAvailable && serial.IsAvailable)
        {
            return serial.NEff < cluster.NEff ? serial : cluster;
        }

        if (cluster.IsAvailable)
        {
            return cluster;
        }

        return serial.IsAvailable ? serial : EffectiveEstimate.Unavailable($"{Strip(cluster.Note)}; {Strip(serial.Note)}");
    }

    private static string Strip(string note) => note.StartsWith("unavailable: ", StringComparison.Ordinal) ? note["unavailable: ".Length..] : note;

    private static EffectiveEstimate From(int n, double nEff, string method, string note)
    {
        if (!double.IsFinite(nEff))
        {
            return EffectiveEstimate.Unavailable("the estimate is not a finite number");
        }

        var clamped = Math.Clamp(nEff, 1, n);
        return new EffectiveEstimate(clamped, n / clamped, method, note);
    }
}
