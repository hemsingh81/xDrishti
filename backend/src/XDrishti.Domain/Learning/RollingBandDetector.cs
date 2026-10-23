using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Rolling expectancy against the band of the reference period (P6-09, ADR 0074). The band is made from the trades known before the monitored period only (frozen: it never moves with the data it judges); a check takes the mean R of the latest
/// <c>window_trades</c> trades known by the check, and the alarm fires after <c>below_band_checks</c> checks in a row below the lower edge. It clears after <c>clear_checks</c> checks in a row at or above the clear level, which lies half way between the edge and
/// the reference mean (hysteresis). Pure and deterministic; trades with the same instant are read together, so a check never sees a trade of a later instant.
/// </summary>
public static class RollingBandDetector
{
    /// <summary>The failure code for a reference that cannot give a band.</summary>
    public const string NoBandCode = "drift.no_band";

    /// <summary>The band of the reference trades; fails with fewer than <c>min_reference_trades</c>, a value that is not a number, or no spread.</summary>
    public static Result<ExpectancyBand> MakeBand(IReadOnlyList<DriftObservation> reference, DriftOptions options)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(options);
        if (reference.Count < options.MinReferenceTrades)
        {
            return Result.Failure<ExpectancyBand>(Error.Validation(NoBandCode, string.Create(CultureInfo.InvariantCulture, $"{reference.Count} reference trades; at least {options.MinReferenceTrades} are needed")));
        }

        if (reference.Any(o => !double.IsFinite(o.R)))
        {
            return Result.Failure<ExpectancyBand>(Error.Validation(NoBandCode, "a reference trade has an R that is not a number"));
        }

        // Summed in time order, so the input order cannot change the last digits of the band.
        reference = DriftSeries.Order(reference);
        var n = reference.Count;
        var mean = reference.Sum(o => o.R) / n;
        var sd = Math.Sqrt(reference.Sum(o => (o.R - mean) * (o.R - mean)) / (n - 1));
        if (sd < 1e-9)
        {
            return Result.Failure<ExpectancyBand>(Error.Validation(NoBandCode, "the reference trades all have the same R, so there is no spread to measure a band by"));
        }

        // The window mean differs from the reference mean by the window's own error and by the error of the estimated reference mean.
        var lower = mean - (options.BandZ * sd * Math.Sqrt((1.0 / options.WindowTrades) + (1.0 / n)));
        return Result.Success(new ExpectancyBand(n, mean, sd, lower, (mean + lower) / 2));
    }

    /// <summary>The alarms of the monitored period: the trades of <paramref name="series"/> known at or after <paramref name="monitorFromEpochSeconds"/>, judged against <paramref name="band"/>.</summary>
    public static IReadOnlyList<DriftEvent> Run(string subject, IReadOnlyList<DriftObservation> series, long monitorFromEpochSeconds, ExpectancyBand band, DriftOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(options);
        var sorted = DriftSeries.Order(series);
        var events = new List<DriftEvent>();
        var c = CultureInfo.InvariantCulture;
        int since = 0, check = 0, below = 0, ok = 0;
        var alarm = false;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].KnownAtEpochSeconds < monitorFromEpochSeconds)
            {
                continue;
            }

            since++;
            if (!DriftSeries.EndsInstant(sorted, i) || since < options.CheckEveryTrades || i + 1 < options.WindowTrades)
            {
                continue;
            }

            since = 0;
            check++;
            var mean = 0.0;
            for (var j = i - options.WindowTrades + 1; j <= i; j++)
            {
                mean += sorted[j].R;
            }

            mean /= options.WindowTrades;
            var at = sorted[i].KnownAtEpochSeconds;
            if (!alarm)
            {
                below = mean < band.Lower ? below + 1 : 0;
                if (below >= options.BelowBandChecks)
                {
                    alarm = true;
                    ok = 0;
                    events.Add(new DriftEvent(DriftDetectorKind.RollingBand, subject, true, at, check, mean, band.Lower, string.Create(c, $"mean R of the last {options.WindowTrades} trades {mean:F3} was below the band edge {band.Lower:F3} (reference mean {band.Mean:F3}, sd {band.Sd:F3}, {band.ReferenceTrades} trades) at {below} checks in a row")));
                }
            }
            else
            {
                ok = mean >= band.ClearLevel ? ok + 1 : 0;
                if (ok >= options.ClearChecks)
                {
                    alarm = false;
                    below = 0;
                    events.Add(new DriftEvent(DriftDetectorKind.RollingBand, subject, false, at, check, mean, band.ClearLevel, string.Create(c, $"mean R of the last {options.WindowTrades} trades {mean:F3} was back at or above the clear level {band.ClearLevel:F3} at {ok} checks in a row")));
                }
            }
        }

        return events;
    }
}
