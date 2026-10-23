using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// A one-sided (downward) CUSUM of R against the reference mean (P6-09, ADR 0074). With z = (mean - R) / sd of the reference, the sum is S = max(0, S + z - k), held at <c>cap</c> times h; it fires above h and clears at or below <c>clear_fraction</c> times h.
/// The sum starts at 0 where the monitored period starts. Results with one instant are all added before the sum is read. Pure and deterministic.
/// </summary>
public static class CusumDetector
{
    /// <summary>The alarms of the monitored period against the band's mean and spread.</summary>
    public static IReadOnlyList<DriftEvent> Run(string subject, IReadOnlyList<DriftObservation> series, long monitorFromEpochSeconds, ExpectancyBand band, DriftOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(options);
        var sorted = DriftSeries.Order(series);
        var events = new List<DriftEvent>();
        var c = CultureInfo.InvariantCulture;
        var h = options.CusumThreshold;
        double s = 0;
        var n = 0;
        var alarm = false;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].KnownAtEpochSeconds < monitorFromEpochSeconds)
            {
                continue;
            }

            n++;
            s = Math.Min(options.CusumCap * h, Math.Max(0, s + ((band.Mean - sorted[i].R) / band.Sd) - options.CusumAllowance));
            if (!DriftSeries.EndsInstant(sorted, i))
            {
                continue;
            }

            var at = sorted[i].KnownAtEpochSeconds;
            if (!alarm && s > h)
            {
                alarm = true;
                events.Add(new DriftEvent(DriftDetectorKind.Cusum, subject, true, at, n, s, h, string.Create(c, $"the downward CUSUM reached {s:F2} (limit {h:F2}, allowance {options.CusumAllowance:F2}) after {n} monitored trades; reference mean {band.Mean:F3} R, sd {band.Sd:F3}")));
            }
            else if (alarm && s <= options.CusumClearFraction * h)
            {
                alarm = false;
                events.Add(new DriftEvent(DriftDetectorKind.Cusum, subject, false, at, n, s, options.CusumClearFraction * h, string.Create(c, $"the downward CUSUM fell back to {s:F2} (clear level {options.CusumClearFraction * h:F2}) after {n} monitored trades")));
            }
        }

        return events;
    }
}
