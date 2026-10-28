namespace XDrishti.Domain.LiveTrading;

/// <summary>Mean, sd, se and the t intervals of the paired shortfalls (spec P11-03). Any interval needs at least <see cref="MinimumForInterval"/> pairs; fewer gives null. Tables: <see cref="StudentT"/> and <see cref="OneSidedT"/> (the nearest lower row, the wider bound).</summary>
public static class ShortfallStats
{
    public const int MinimumForInterval = 10;

    public static ShortfallSummary? Summarise(IReadOnlyList<double> shortfalls, double margin, int minPairs)
    {
        ArgumentNullException.ThrowIfNull(shortfalls);
        var n = shortfalls.Count;
        if (n < MinimumForInterval || shortfalls.Any(v => !double.IsFinite(v)))
        {
            return null;
        }

        var mean = shortfalls.Sum() / n;
        var sd = Math.Sqrt(shortfalls.Sum(v => (v - mean) * (v - mean)) / (n - 1));
        var se = sd / Math.Sqrt(n);
        var half = StudentT.Quantile95(n - 1) * se;
        var oneSided = OneSidedT.Quantile95(n - 1) * se;
        return new ShortfallSummary(n, mean, sd, se, mean - half, mean + half, mean + oneSided, mean - oneSided, NonInferiority.Verdict(n, mean + oneSided, mean - oneSided, margin, minPairs));
    }
}
