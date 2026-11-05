namespace XDrishti.Domain.Learning;

/// <summary>
/// Platt scaling of a raw score into a probability (CF-10 part 10a, ADR 0214): <c>p = sigmoid(Slope x (score - Center) + Intercept)</c>. Two parameters are fitted (<see cref="Slope"/>, <see cref="Intercept"/>); <see cref="Center"/> is only the mean of the fit scores,
/// kept so the arithmetic stays well scaled and a score that is not a number reads as the fitted mean level. The targets are Platt's smoothed ones, <c>y+ = (N+ + 1) / (N+ + 2)</c> for a win and <c>y- = 1 / (N- + 2)</c> for a loss, so the fit is finite even when the scores
/// separate the classes. The fit is a deterministic Newton iteration with a backtracking step (sequential sums, fixed limits). The slope is never negative: a fit that would fall with the score is replaced by the constant that fits best, so the map never reorders rows.
/// </summary>
public sealed class PlattCalibrator
{
    private const int MaxIterations = 100;
    private const int MaxHalvings = 40;
    private const double Tolerance = 1e-12;

    private PlattCalibrator(double slope, double center, double intercept)
    {
        Slope = slope;
        Center = center;
        Intercept = intercept;
    }

    /// <summary>The slope in probability-logit per unit of raw score; at least 0.</summary>
    public double Slope { get; }

    /// <summary>The mean of the fit scores (a centring constant, not a fitted parameter).</summary>
    public double Center { get; }

    /// <summary>The logit of the probability at <see cref="Center"/>.</summary>
    public double Intercept { get; }

    /// <summary>A constant map: every score gives <paramref name="rate"/> (within 1e-9 to 1 - 1e-9).</summary>
    public static PlattCalibrator Constant(double rate)
    {
        if (!double.IsFinite(rate))
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        return new PlattCalibrator(0, 0, Logit(Math.Clamp(rate, 1e-9, 1 - 1e-9)));
    }

    /// <summary>A map from stored parameters (a model loaded from its artifact); the slope must not be negative and all three must be finite.</summary>
    public static PlattCalibrator FromParameters(double slope, double center, double intercept)
    {
        if (!double.IsFinite(slope) || slope < 0 || !double.IsFinite(center) || !double.IsFinite(intercept))
        {
            throw new ArgumentException("Platt parameters must be finite and the slope not negative.", nameof(slope));
        }

        return new PlattCalibrator(slope, center, intercept);
    }

    /// <summary>Fits the map to scores the scorer was not trained on; non-finite scores are skipped; with no usable pair the map is the constant 0.5.</summary>
    /// <param name="scores">The raw scores.</param>
    /// <param name="labels">The labels, 0 or 1, one per score.</param>
    public static PlattCalibrator Fit(IReadOnlyList<double> scores, IReadOnlyList<int> labels)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(labels);
        if (scores.Count != labels.Count)
        {
            throw new ArgumentException("scores and labels must have the same length.", nameof(labels));
        }

        var used = Enumerable.Range(0, scores.Count).Where(i => double.IsFinite(scores[i])).ToArray();
        if (used.Length == 0)
        {
            return Constant(0.5);
        }

        var positives = used.Count(i => labels[i] == 1);
        var negatives = used.Length - positives;
        var up = (positives + 1.0) / (positives + 2.0);
        var down = 1.0 / (negatives + 2.0);
        var targets = used.Select(i => labels[i] == 1 ? up : down).ToArray();
        var meanTarget = targets.Sum() / targets.Length;
        var center = used.Sum(i => scores[i]) / used.Length;
        var spread = Math.Sqrt(used.Sum(i => (scores[i] - center) * (scores[i] - center)) / used.Length);
        var constant = new PlattCalibrator(0, center, Logit(meanTarget));
        if (!(spread > 1e-12))
        {
            return constant;
        }

        var z = used.Select(i => (scores[i] - center) / spread).ToArray();
        var (a, b) = Newton(z, targets, Logit(meanTarget));
        return a > 0 && double.IsFinite(a) && double.IsFinite(b) ? new PlattCalibrator(a / spread, center, b) : constant;
    }

    /// <summary>The probability of a raw score; a non-finite score reads as the fitted level at the centre.</summary>
    public double Apply(double score) => Sigmoid(double.IsFinite(score) ? (Slope * (score - Center)) + Intercept : Intercept);

    private static (double A, double B) Newton(double[] z, double[] t, double start)
    {
        double a = 0, b = start;
        var loss = Loss(z, t, a, b);
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            double gA = 0, gB = 0, hAA = 0, hAB = 0, hBB = 0;
            for (var i = 0; i < z.Length; i++)
            {
                var p = Sigmoid((a * z[i]) + b);
                var d = p - t[i];
                var w = p * (1 - p);
                gA += d * z[i];
                gB += d;
                hAA += w * z[i] * z[i];
                hAB += w * z[i];
                hBB += w;
            }

            var det = (hAA * hBB) - (hAB * hAB);
            if (!(det > 1e-18))
            {
                break;
            }

            var dA = -((hBB * gA) - (hAB * gB)) / det;
            var dB = -((hAA * gB) - (hAB * gA)) / det;
            var slope = (gA * dA) + (gB * dB);
            var step = 1.0;
            var moved = false;
            for (var h = 0; h < MaxHalvings; h++, step /= 2)
            {
                var next = Loss(z, t, a + (step * dA), b + (step * dB));
                if (next <= loss + (1e-4 * step * slope))
                {
                    a += step * dA;
                    b += step * dB;
                    loss = next;
                    moved = true;
                    break;
                }
            }

            if (!moved || (Math.Abs(step * dA) + Math.Abs(step * dB)) < Tolerance)
            {
                break;
            }
        }

        return (a, b);
    }

    private static double Loss(double[] z, double[] t, double a, double b)
    {
        var sum = 0.0;
        for (var i = 0; i < z.Length; i++)
        {
            var x = (a * z[i]) + b;
            sum += Softplus(x) - (t[i] * x);
        }

        return sum;
    }

    private static double Sigmoid(double x) => x >= 0 ? 1 / (1 + Math.Exp(-x)) : Math.Exp(x) / (1 + Math.Exp(x));

    private static double Softplus(double x) => x > 0 ? x + Math.Log(1 + Math.Exp(-x)) : Math.Log(1 + Math.Exp(x));

    private static double Logit(double p) => Math.Log(p / (1 - p));
}
