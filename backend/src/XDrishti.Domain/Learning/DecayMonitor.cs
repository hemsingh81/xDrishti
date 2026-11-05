using XDrishti.Domain.Confidence;
using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The decay monitor of a forward series (spec CF-12, D1; ADR 0218). Over non-overlapping windows of <c>WindowTrades</c> trades it alerts when a window's mean R is strictly below <c>mu_ref - z * sigma * sqrt(DEFF / n + 1 / N_ref)</c>: the window's own error
/// widened by the design effect of its days (CF-01, same-day candidates are correlated) plus the error of the estimated reference mean (the second term P6-09's band also carries). Windows do not overlap, so one run of bad trades is one event, and "twice in a row"
/// means two consecutive completed windows below their thresholds; a window above in between resets it. The P6-09 CUSUM is run on the same series by the existing <see cref="CusumDetector"/> (not copied) and a by-quarter table is added. The reference
/// is the <see cref="ExpectancyBand"/> of P6-09 (<see cref="RollingBandDetector.MakeBand"/>): its mean, sd and trade count. Pure: no clock, no random source; the input order does not matter.
/// </summary>
public static class DecayMonitor
{
    private const string Subject = "decay";

    /// <summary>The alert level of a window: <c>muRef - sigmas * sigma * sqrt(deff / window + 1 / nRef)</c>.</summary>
    public static double Threshold(double muRef, double sigma, int nRef, int window, double deff, double sigmas)
    {
        if (window < 1 || nRef < 1 || !(deff > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The window, the reference count and the design effect must be positive.");
        }

        return muRef - (sigmas * sigma * Math.Sqrt((deff / window) + (1.0 / nRef)));
    }

    /// <summary>Reads the series. <paramref name="rho"/> is the same-day correlation of the design effect (0 to 1).</summary>
    public static DecayReport Evaluate(IReadOnlyList<DecayTrade> series, ExpectancyBand reference, DecayOptions options, double rho, DriftOptions drift)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(drift);
        if (options.Problems().Count > 0)
        {
            throw new ArgumentException("The decay options are out of range: " + string.Join("; ", options.Problems()), nameof(options));
        }

        if (!double.IsFinite(rho) || rho is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rho), "The correlation must be between 0 and 1.");
        }

        foreach (var t in series)
        {
            if (!double.IsFinite(t.R) || t.DayCandidates < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(series), "A trade's R must be finite and its day must have at least one candidate.");
            }
        }

        var sorted = series.OrderBy(t => t.KnownAtEpochSeconds).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
        var w = options.WindowTrades;
        var windows = new List<DecayWindow>();
        int streak = 0;
        int? firstTwice = null;
        for (var start = 0; start + w <= sorted.Count; start += w)
        {
            double sum = 0, deffSum = 0;
            for (var i = start; i < start + w; i++)
            {
                sum += sorted[i].R;
                deffSum += EdgeCount.DesignEffect(sorted[i].DayCandidates, rho);
            }

            var deff = deffSum / w;
            var mean = sum / w;
            var threshold = Threshold(reference.Mean, reference.Sd, reference.ReferenceTrades, w, deff, options.Sigmas);
            var below = mean < threshold;
            var index = windows.Count + 1;
            windows.Add(new DecayWindow(index, w, sorted[start].Day, sorted[start + w - 1].Day, mean, deff, threshold, below));
            streak = below ? streak + 1 : 0;
            if (streak >= 2)
            {
                firstTwice ??= index;
            }
        }

        var observations = sorted.Select(t => new DriftObservation(t.Key, t.KnownAtEpochSeconds, t.R)).ToList();
        var cusum = CusumDetector.Run(Subject, observations, 0, reference, drift);
        var quarters = sorted
            .GroupBy(t => (t.Day.Year, Quarter: ((t.Day.Month - 1) / 3) + 1))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Quarter)
            .Select(g =>
            {
                var mean = g.Sum(t => t.R) / g.Count();
                return new DecayQuarter(g.Key.Year, g.Key.Quarter, g.Count(), mean, mean < options.QuarterFloorR);
            })
            .ToList();
        return new DecayReport(
            sorted.Count, sorted.Count - (windows.Count * w), windows.AsReadOnly(), streak, streak >= 2, firstTwice, cusum, cusum.Count > 0 && cusum[^1].Fired, quarters.AsReadOnly(),
            quarters.Count == 0 ? null : (double)quarters.Count(q => q.Weak) / quarters.Count);
    }
}
