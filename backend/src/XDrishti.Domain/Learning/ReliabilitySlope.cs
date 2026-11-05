using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The reliability slope of spec CF-12, D6 (ADR 0218): a two-parameter logistic regression (intercept and slope) of the outcome on <c>logit(p_hat)</c> over the trailing <c>SlopeWindow</c> outcomes (200), fitted by Newton's method from the ideal start (0, 1).
/// A probability is held inside [1e-6, 1 - 1e-6] before the logit. It alarms when the slope is below 0.8 or above 1.2 <b>and</b> farther than 2 standard errors from 1, and never with fewer outcomes than the window (the slope is then shown only). The rolling ECE
/// and the gap test stay P6-09's (<see cref="CalibrationDriftDetector"/>, unchanged). An alarm here is evidence for a CF-10 rollback proposal; this slice rolls nothing back. Pure.
/// </summary>
public static class ReliabilitySlope
{
    private const double Clip = 1e-6;

    /// <summary>Fits the slope over the trailing window of <paramref name="outcomes"/> (ordered by the instant the label was known, then key).</summary>
    public static ReliabilityResult Evaluate(IReadOnlyList<CalibrationObservation> outcomes, LoopOptions options)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Problems().Count > 0)
        {
            throw new ArgumentException("The loop options are out of range: " + string.Join("; ", options.Problems()), nameof(options));
        }

        if (outcomes.Any(o => !double.IsFinite(o.Predicted) || o.Predicted is < 0 or > 1 || o.Label is not (0 or 1)))
        {
            return Unavailable(0, "an outcome has a probability outside 0 to 1 or a label other than 0 or 1");
        }

        var sorted = DriftSeries.Order(outcomes);
        var window = sorted.Skip(Math.Max(0, sorted.Count - LoopOptions.SlopeWindow)).ToList();
        var n = window.Count;
        var wins = window.Sum(o => o.Label);
        if (n == 0 || wins == 0 || wins == n)
        {
            return Unavailable(n, "the window needs outcomes of both kinds");
        }

        var x = window.Select(o => Logit(o.Predicted)).ToArray();
        var y = window.Select(o => (double)o.Label).ToArray();
        double a = 0, b = 1;
        var converged = false;
        double s0 = 0, s1 = 0, s2 = 0, det = 0;
        for (var iteration = 0; iteration < 100 && !converged; iteration++)
        {
            double g0 = 0, g1 = 0;
            s0 = s1 = s2 = 0;
            for (var i = 0; i < n; i++)
            {
                var p = 1 / (1 + Math.Exp(-(a + (b * x[i]))));
                var w = p * (1 - p);
                g0 += y[i] - p;
                g1 += (y[i] - p) * x[i];
                s0 += w;
                s1 += w * x[i];
                s2 += w * x[i] * x[i];
            }

            det = (s0 * s2) - (s1 * s1);
            if (!double.IsFinite(det) || det < 1e-12)
            {
                return Unavailable(n, "the predictions do not vary enough (or the outcomes are separated perfectly) for a slope to be fitted");
            }

            var da = ((s2 * g0) - (s1 * g1)) / det;
            var db = ((s0 * g1) - (s1 * g0)) / det;
            a += da;
            b += db;
            if (!double.IsFinite(a) || !double.IsFinite(b))
            {
                return Unavailable(n, "the fit did not converge");
            }

            converged = Math.Abs(da) < 1e-10 && Math.Abs(db) < 1e-10;
        }

        if (!converged)
        {
            return Unavailable(n, "the fit did not converge");
        }

        var se = Math.Sqrt(s0 / det);
        var outside = b < options.SlopeLow || b > options.SlopeHigh;
        var far = Math.Abs(b - 1) > options.SlopeSe * se;
        var state = n < LoopOptions.SlopeWindow ? ReliabilityState.ShownOnly : outside && far ? ReliabilityState.Alarm : ReliabilityState.Ok;
        var ci = CultureInfo.InvariantCulture;
        var text = string.Create(ci, $"reliability slope {b:F3} (standard error {se:F3}, intercept {a:F3}) over the last {n} outcomes; ideal 1, band {options.SlopeLow:0.0#} to {options.SlopeHigh:0.0#} and {options.SlopeSe:0.#} standard errors from 1")
            + state switch
            {
                ReliabilityState.ShownOnly => string.Create(ci, $"; fewer than {LoopOptions.SlopeWindow} outcomes, shown and never an alarm"),
                ReliabilityState.Alarm => b < 1 ? "; alarm: the probabilities are overconfident" : "; alarm: the probabilities are underconfident",
                _ => "; no alarm",
            };
        return new ReliabilityResult(state, n, b, a, se, text);
    }

    private static double Logit(double p)
    {
        var c = Math.Min(1 - Clip, Math.Max(Clip, p));
        return Math.Log(c / (1 - c));
    }

    private static ReliabilityResult Unavailable(int n, string reason) => new(ReliabilityState.Unavailable, n, null, null, null, "unavailable: " + reason);
}
