using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The expected calibration error with an interval (CF-10 part 10a, ADR 0214, decision D1): whole trade days are resampled with replacement (trades of one day are not independent), the error is recomputed on each resample, and the interval is the percentile interval of those errors.
/// The generator is written out (<see cref="SeededRandom"/>), so a seed gives the same draws on every machine. The error is biased upward on few rows (noise alone gives a positive error) and the bootstrap carries that bias along: the interval says how much the figure could move,
/// it does not remove the bias. Block length is one day (serial correlation across days is ignored). Pure and deterministic.
/// </summary>
public static class EceInterval
{
    /// <summary>The failure code for input that cannot be read.</summary>
    public const string InvalidCode = "calibration.invalid";

    /// <summary>Assesses predictions against labels, with the trade day of each outcome.</summary>
    /// <param name="predicted">Predicted probabilities, in [0, 1].</param>
    /// <param name="labels">Labels, 0 or 1.</param>
    /// <param name="days">The trade day of each outcome.</param>
    /// <param name="draws">Resamples, 100 to 20000.</param>
    /// <param name="level">The interval's coverage, above 0.5 and below 1.</param>
    /// <param name="bins">Equal-mass calibration bins.</param>
    /// <param name="seedKey">What the seed is made of (the sha256 of this text): the model and the purpose, so a rerun repeats.</param>
    public static Result<CalibrationAssessment> Assess(IReadOnlyList<double> predicted, IReadOnlyList<int> labels, IReadOnlyList<DateOnly> days, int draws, double level, int bins, string seedKey)
    {
        ArgumentNullException.ThrowIfNull(predicted);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedKey);
        if (predicted.Count == 0 || predicted.Count != labels.Count || predicted.Count != days.Count)
        {
            return Fail("predictions, labels and days must be the same non-zero length");
        }

        if (predicted.Any(p => !double.IsFinite(p) || p is < 0 or > 1) || labels.Any(l => l is not (0 or 1)))
        {
            return Fail("a prediction is outside 0 to 1 or a label is not 0 or 1");
        }

        if (draws is < 100 or > 20000 || !(level > 0.5 && level < 1) || bins < 1)
        {
            return Fail("draws must be 100 to 20000, level above 0.5 and below 1, bins at least 1");
        }

        var n = predicted.Count;
        var wins = labels.Sum();
        var meanPredicted = predicted.Sum() / n;
        var observed = (double)wins / n;
        var metrics = MetricsCalculator.Compute(predicted, labels, bins);
        var baseRateLoss = observed is 0 or 1 ? 0 : -((observed * Math.Log(observed)) + ((1 - observed) * Math.Log(1 - observed)));

        var byDay = days.Select((d, i) => (Day: d, Index: i)).GroupBy(t => t.Day).OrderBy(g => g.Key).Select(g => g.Select(t => t.Index).ToArray()).ToArray();
        var random = new SeededRandom(SeededRandom.SeedOf("ece|" + seedKey));
        var errors = new double[draws];
        var p = new List<double>(n + 16);
        var l = new List<int>(n + 16);
        for (var d = 0; d < draws; d++)
        {
            p.Clear();
            l.Clear();
            for (var k = 0; k < byDay.Length; k++)
            {
                foreach (var i in byDay[random.Next(byDay.Length)])
                {
                    p.Add(predicted[i]);
                    l.Add(labels[i]);
                }
            }

            errors[d] = MetricsCalculator.EcePp(p, l, bins);
        }

        Array.Sort(errors);
        var low = errors[(int)Math.Floor((1 - level) / 2 * (draws - 1))];
        var high = errors[(int)Math.Ceiling((1 + level) / 2 * (draws - 1))];
        var se = Math.Sqrt(observed * (1 - observed) / n) * 100;
        return Result.Success(new CalibrationAssessment(n, byDay.Length, meanPredicted, observed, (meanPredicted - observed) * 100, se, metrics.EcePp, low, high, level, draws, metrics.LogLoss, baseRateLoss));
    }

    private static Result<CalibrationAssessment> Fail(string message) => Result.Failure<CalibrationAssessment>(Error.Validation(InvalidCode, message));
}
