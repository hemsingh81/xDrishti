namespace XDrishti.Domain.Market.History;

/// <summary>The limits of the overlap probe (decision 9): pass when the median absolute close difference is at most <see cref="MedianBps"/> and at least <see cref="MinWithinMaxPct"/> percent of the bars are within <see cref="MaxBps"/>.</summary>
public sealed record SeamProbeLimits(double MedianBps, double MaxBps, double MinWithinMaxPct)
{
    public static SeamProbeLimits Default { get; } = new(5, 50, 99);
}

public enum SeamProbeVerdict
{
    /// <summary>Dhan's bars, adjusted by the seam rule, match the CSV bars.</summary>
    Passed,

    /// <summary>They do not match: the instrument is <c>seam_unresolved</c> and is not backfilled.</summary>
    Failed,

    /// <summary>No minute is in both series: nothing was proven.</summary>
    NoOverlap,
}

public sealed record SeamProbeResult(SeamProbeVerdict Verdict, long Compared, double MedianBps, double WithinMaxPct);

/// <summary>
/// Compares Dhan's close prices (already adjusted by <see cref="SourceAwareFactors"/> as if they lay before the seam) with the CSV close prices of the same minutes, in basis points of the CSV close. The tolerances are the settings; nothing is read or written here. Pure.
/// </summary>
public static class SeamProbe
{
    /// <summary>Both lists have the same length and are paired by minute: <c>dhanAdjusted[i]</c> against <c>csv[i]</c>.</summary>
    public static SeamProbeResult Evaluate(IReadOnlyList<double> dhanAdjustedCloses, IReadOnlyList<double> csvCloses, SeamProbeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(dhanAdjustedCloses);
        ArgumentNullException.ThrowIfNull(csvCloses);
        ArgumentNullException.ThrowIfNull(limits);
        if (dhanAdjustedCloses.Count != csvCloses.Count)
        {
            throw new ArgumentException("The two series must be paired by minute.", nameof(csvCloses));
        }

        var diffs = new List<double>(csvCloses.Count);
        for (var i = 0; i < csvCloses.Count; i++)
        {
            if (csvCloses[i] > 0)
            {
                diffs.Add(Math.Abs(dhanAdjustedCloses[i] - csvCloses[i]) / csvCloses[i] * 10_000);
            }
        }

        if (diffs.Count == 0)
        {
            return new SeamProbeResult(SeamProbeVerdict.NoOverlap, 0, 0, 0);
        }

        diffs.Sort();
        var median = diffs.Count % 2 == 1 ? diffs[diffs.Count / 2] : (diffs[(diffs.Count / 2) - 1] + diffs[diffs.Count / 2]) / 2;
        var within = diffs.Count(d => d <= limits.MaxBps + 1e-9);
        var withinPct = within * 100.0 / diffs.Count;
        var passed = median <= limits.MedianBps + 1e-9 && withinPct >= limits.MinWithinMaxPct - 1e-9;
        return new SeamProbeResult(passed ? SeamProbeVerdict.Passed : SeamProbeVerdict.Failed, diffs.Count, median, withinPct);
    }
}
