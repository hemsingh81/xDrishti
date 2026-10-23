using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Calibration drift of a registered model on rolling windows (P6-09, ADR 0074). A check takes the latest <c>calibration_window</c> outcomes known by the check and compares the mean predicted probability with the observed win rate, in percentage points.
/// A breach needs the gap to exceed both the stated limit (<c>max_calibration_gap_pp</c>) and <c>calibration_z</c> standard errors of the observed rate (the sum of p(1-p) under the predictions), so noise alone rarely breaches. The alarm fires after
/// <c>calibration_breach_checks</c> breaches in a row and clears after <c>clear_checks</c> checks in a row with the gap at or under half of the breach limit. The evidence also carries the window's expected calibration error and Brier score
/// (<see cref="MetricsCalculator"/>). Pure and deterministic.
/// </summary>
public static class CalibrationDriftDetector
{
    /// <summary>The failure code for input that cannot be read.</summary>
    public const string InvalidCode = "drift.invalid";

    /// <summary>The alarms over the outcomes, which must all be outcomes the model was not trained on.</summary>
    public static Result<IReadOnlyList<DriftEvent>> Run(string subject, IReadOnlyList<CalibrationObservation> series, DriftOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        if (series.Any(o => !double.IsFinite(o.Predicted) || o.Predicted is < 0 or > 1 || o.Label is not (0 or 1)))
        {
            return Result.Failure<IReadOnlyList<DriftEvent>>(Error.Validation(InvalidCode, "an outcome has a probability outside 0 to 1 or a label other than 0 or 1"));
        }

        var sorted = DriftSeries.Order(series);
        var events = new List<DriftEvent>();
        var c = CultureInfo.InvariantCulture;
        var w = options.CalibrationWindow;
        int since = 0, check = 0, breaches = 0, ok = 0;
        var alarm = false;
        for (var i = 0; i < sorted.Count; i++)
        {
            since++;
            if (!DriftSeries.EndsInstant(sorted, i) || since < options.CalibrationCheckEvery || i + 1 < w)
            {
                continue;
            }

            since = 0;
            check++;
            var scores = new double[w];
            var labels = new int[w];
            double predicted = 0, variance = 0;
            var wins = 0;
            for (var j = 0; j < w; j++)
            {
                var o = sorted[i - w + 1 + j];
                scores[j] = o.Predicted;
                labels[j] = o.Label;
                predicted += o.Predicted;
                variance += o.Predicted * (1 - o.Predicted);
                wins += o.Label;
            }

            var gapPp = ((predicted / w) - ((double)wins / w)) * 100;
            var limit = Math.Max(options.MaxCalibrationGapPp, options.CalibrationZ * 100 * Math.Sqrt(variance) / w);
            var at = sorted[i].KnownAtEpochSeconds;
            var gap = Math.Abs(gapPp);
            string Text(string lead)
            {
                var m = MetricsCalculator.Compute(scores, labels, 5);
                return string.Create(c, $"{lead}: mean predicted {predicted / w * 100:F1} % against observed {(double)wins / w * 100:F1} % over the last {w} outcomes (gap {gapPp:+0.0;-0.0} pp, limit {limit:F1} pp); window ECE {m.EcePp:F1} pp, Brier {m.Brier:F3}");
            }

            if (!alarm)
            {
                breaches = gap > limit ? breaches + 1 : 0;
                if (breaches >= options.CalibrationBreachChecks)
                {
                    alarm = true;
                    ok = 0;
                    events.Add(new DriftEvent(DriftDetectorKind.Calibration, subject, true, at, check, gapPp, limit, Text(string.Create(c, $"calibration beyond the limit at {breaches} checks in a row"))));
                }
            }
            else
            {
                ok = gap <= limit / 2 ? ok + 1 : 0;
                if (ok >= options.ClearChecks)
                {
                    alarm = false;
                    breaches = 0;
                    events.Add(new DriftEvent(DriftDetectorKind.Calibration, subject, false, at, check, gapPp, limit / 2, Text(string.Create(c, $"calibration back within half the limit at {ok} checks in a row"))));
                }
            }
        }

        return Result.Success<IReadOnlyList<DriftEvent>>(events);
    }
}
