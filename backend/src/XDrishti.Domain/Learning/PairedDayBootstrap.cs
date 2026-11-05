using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The paired day-cluster bootstrap (CF-10 part 10c-1, ADR 0216, spec D4; reused by CF-12): given the gain of a model over a baseline on each of the same rows (baseline loss minus model loss, positive is better) and the trade day of each row, whole days are resampled with
/// replacement (the trades of a day are not independent), the mean gain is recomputed on each resample, and the result is the share of resamples with a mean above zero and the percentile interval of the mean gain.
/// Behaviour that is documented and tested: a resample mean at or below zero (within 1e-12) is not "better", so a tie counts against; fewer than <c>minDays</c> distinct days is not available (the resamples would repeat a few values and the share would be 0 or 1);
/// gains that are all equal give a probability of 0 or 1 and an interval of one point; input order does not matter except for rounding in the last digits of a sum. Seeded (<see cref="SeededRandom"/>), so the same rows and key repeat on every machine.
/// Cost: <c>draws x days</c> random picks and no allocation per draw; the day totals are built once. Block length is one day, so serial correlation across days is ignored. Pure.
/// </summary>
public static class PairedDayBootstrap
{
    /// <summary>The failure code for input that cannot be read.</summary>
    public const string InvalidCode = "bootstrap.invalid";

    /// <summary>The failure code when there are fewer days than asked for.</summary>
    public const string TooFewDaysCode = "bootstrap.too_few_days";

    private const double Tie = 1e-12;

    /// <summary>Assesses a gain.</summary>
    /// <param name="gains">The gain of each row (finite): baseline loss minus model loss.</param>
    /// <param name="days">The trade day of each row, same length.</param>
    /// <param name="draws">Resamples, 100 to 20000.</param>
    /// <param name="level">The interval's coverage, above 0.5 and below 1.</param>
    /// <param name="seedKey">What the seed is made of (the sha256 of this text): the model, the baseline and the span, so a rerun repeats.</param>
    /// <param name="minDays">The fewest distinct days, at least 2.</param>
    public static Result<PairedBootstrapResult> Assess(IReadOnlyList<double> gains, IReadOnlyList<DateOnly> days, int draws, double level, string seedKey, int minDays = 2)
    {
        ArgumentNullException.ThrowIfNull(gains);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedKey);
        if (gains.Count == 0 || gains.Count != days.Count)
        {
            return Fail(InvalidCode, "gains and days must be the same non-zero length");
        }

        if (draws is < 100 or > 20000 || !(level > 0.5 && level < 1) || minDays < 2)
        {
            return Fail(InvalidCode, "draws must be 100 to 20000, level above 0.5 and below 1, minDays at least 2");
        }

        foreach (var g in gains)
        {
            if (!double.IsFinite(g))
            {
                return Fail(InvalidCode, "a gain is not a finite number");
            }
        }

        var distinct = days.Distinct().Order().ToArray();
        if (distinct.Length < minDays)
        {
            return Fail(TooFewDaysCode, "fewer trade days than the figure needs");
        }

        var index = new Dictionary<DateOnly, int>(distinct.Length);
        for (var i = 0; i < distinct.Length; i++)
        {
            index[distinct[i]] = i;
        }

        var sums = new double[distinct.Length];
        var counts = new int[distinct.Length];
        double total = 0;
        for (var i = 0; i < gains.Count; i++)
        {
            var d = index[days[i]];
            sums[d] += gains[i];
            counts[d]++;
            total += gains[i];
        }

        // shortcut: block length fixed at one day (spec block_days); add the setting when serial correlation across days is measured
        var random = new SeededRandom(SeededRandom.SeedOf("paired|" + seedKey));
        var means = new double[draws];
        var better = 0;
        for (var d = 0; d < draws; d++)
        {
            double sum = 0;
            long count = 0;
            for (var k = 0; k < distinct.Length; k++)
            {
                var pick = random.Next(distinct.Length);
                sum += sums[pick];
                count += counts[pick];
            }

            means[d] = sum / count;
            if (means[d] > Tie)
            {
                better++;
            }
        }

        Array.Sort(means);
        var low = means[(int)Math.Floor((1 - level) / 2 * (draws - 1))];
        var high = means[(int)Math.Ceiling((1 + level) / 2 * (draws - 1))];
        return Result.Success(new PairedBootstrapResult(gains.Count, distinct.Length, draws, level, total / gains.Count, (double)better / draws, low, high));
    }

    private static Result<PairedBootstrapResult> Fail(string code, string message) => Result.Failure<PairedBootstrapResult>(Error.Validation(code, message));
}
