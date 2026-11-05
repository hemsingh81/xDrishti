using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The statistics of the random-entry null (spec CF-07, D1): given the mean net R of the real out-of-sample trades and the mean net R of each seed (one full set of random-entry trades with the same instrument, session, side, risk fraction, target
/// multiple and costs), it reads the empirical percentiles and the p-value <c>(count at or above the real mean + 1) / (seeds + 1)</c>. The real mean passes when and only when <c>p &lt;= 1 - percentile / 100</c> (review fix 1: the percentile is displayed,
/// not a second test). Pure, no random source: the seeds are drawn elsewhere. <see cref="NullStatus.Unavailable"/> is never a pass.
/// </summary>
public static class RandomEntryNull
{
    /// <summary>Fewer real trades than this and the null is unavailable: a mean of fewer trades does not separate from a seed mean.</summary>
    public const int MinTrades = 30;

    /// <summary>More than this share of the draws dropped (no trade after the redraws) and the null is unavailable: the random set no longer looks like the real one.</summary>
    public const double MaxDroppedShare = 0.2;

    /// <param name="realMeanR">Mean net R of the real trades the null was made for.</param>
    /// <param name="trades">How many real trades that is.</param>
    /// <param name="draws">Seeds asked for (a seed with no random trade at all would need every draw of it dropped, which the dropped share already refuses).</param>
    /// <param name="seedMeans">Mean net R of each seed that made at least one trade (any order).</param>
    /// <param name="droppedShare">Share of the (trade, seed) draws dropped after the redraws, 0 to 1.</param>
    /// <param name="options">The percentile, which sets the level of the test.</param>
    public static RandomEntryResult Evaluate(double realMeanR, int trades, int draws, IReadOnlyList<double> seedMeans, double droppedShare, NullOptions options)
    {
        ArgumentNullException.ThrowIfNull(seedMeans);
        ArgumentNullException.ThrowIfNull(options);
        var alpha = options.Alpha;
        RandomEntryResult Unavailable(string reason) => new(trades, draws, seedMeans.Count, realMeanR, null, null, null, null, null, options.Percentile, null, 0, null, alpha, NullStatus.Unavailable, "unavailable: " + reason);

        if (trades < MinTrades)
        {
            return Unavailable("fewer than " + MinTrades.ToString(CultureInfo.InvariantCulture) + " out-of-sample trades could be given a random entry");
        }

        if (!double.IsFinite(realMeanR) || seedMeans.Any(m => !double.IsFinite(m)))
        {
            return Unavailable("a result is not a finite number");
        }

        if (!(droppedShare <= MaxDroppedShare))
        {
            return Unavailable("more than " + (MaxDroppedShare * 100).ToString("0", CultureInfo.InvariantCulture) + " % of the random entries were dropped after the redraws");
        }

        var sorted = seedMeans.Order().ToArray();
        var atLeast = seedMeans.Count(m => m >= realMeanR);
        var p = (atLeast + 1.0) / (sorted.Length + 1.0);
        var pass = p <= alpha;
        var reason = string.Create(CultureInfo.InvariantCulture, $"{atLeast} of {sorted.Length} seeds are at or above the real mean {realMeanR:0.####} R, p = {p:0.####} against {alpha:0.####}");
        return new RandomEntryResult(
            trades, draws, sorted.Length, realMeanR, sorted.Average(), MonteCarlo.Percentile(sorted, 0.05), MonteCarlo.Percentile(sorted, 0.5), MonteCarlo.Percentile(sorted, 0.95), MonteCarlo.Percentile(sorted, 0.99),
            options.Percentile, MonteCarlo.Percentile(sorted, options.Percentile / 100), atLeast, p, alpha, pass ? NullStatus.Pass : NullStatus.Fail, reason);
    }
}
