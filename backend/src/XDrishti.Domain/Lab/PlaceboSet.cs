using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The rank test of the placebo set (spec CF-07, D2): the real strategy must beat every comparable placebo (the mirror, the later entries, the other sessions and the other instruments), so with 20 comparable placebos it must rank first of 21
/// (<c>1/21 = 0.0476</c>, at most 0.05). A placebo that could not be computed (too few trades placed) is left out and reduces the number compared; with fewer than <see cref="NullOptions.MinPlacebos"/> comparable placebos the test is inconclusive
/// (<see cref="NullStatus.Unavailable"/>), whatever the ranks. A tie goes against the strategy. Pure.
/// </summary>
public static class PlaceboSet
{
    /// <param name="realMeanR">Mean net R of the real trades.</param>
    /// <param name="realTrades">How many real trades that mean is over.</param>
    /// <param name="minPlaced">The fewest trades a shape must place to be compared (so a mean of three trades never ranks against a mean of three hundred).</param>
    /// <param name="readings">What each shape gave, in the fixed order.</param>
    /// <param name="options">The fewest comparable placebos (the level is the fixed <see cref="NullOptions.PlaceboAlpha"/>, not the percentile of the random-entry null).</param>
    public static PlaceboSetResult Evaluate(double realMeanR, int realTrades, int minPlaced, IReadOnlyList<PlaceboReading> readings, NullOptions options)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(options);
        var alpha = NullOptions.PlaceboAlpha;
        var c = CultureInfo.InvariantCulture;
        var comparable = readings.Select(r => r.MeanR is { } m && double.IsFinite(m) && r.Placed >= minPlaced).ToArray();
        var k = comparable.Count(x => x);
        var finiteReal = double.IsFinite(realMeanR);

        PlaceboSetResult Unavailable(string reason) => new(readings.Count, k, options.MinPlacebos, realMeanR, null, null, alpha, NullStatus.Unavailable, "unavailable: " + reason,
            [.. readings.Select((r, i) => new PlaceboRow(r.Name, r.Placed, r.MeanR, null, comparable[i], comparable[i] ? null : NotComparable(r, minPlaced)))]);

        if (!finiteReal || realTrades < minPlaced)
        {
            return Unavailable("only " + realTrades.ToString(c) + " real trades could be restated (at least " + minPlaced.ToString(c) + " are needed)");
        }

        if (k < options.MinPlacebos)
        {
            return Unavailable("inconclusive: " + k.ToString(c) + " of " + readings.Count.ToString(c) + " placebos could be compared (at least " + options.MinPlacebos.ToString(c) + " are needed)");
        }

        var atLeast = Enumerable.Range(0, readings.Count).Count(i => comparable[i] && readings[i].MeanR!.Value >= realMeanR);
        var rank = atLeast + 1;
        var p = rank / (k + 1.0);
        var pass = p <= alpha;
        var rows = readings.Select((r, i) => new PlaceboRow(r.Name, r.Placed, r.MeanR, comparable[i] ? 1 + Enumerable.Range(0, readings.Count).Count(j => j != i && comparable[j] && readings[j].MeanR!.Value > r.MeanR!.Value) + (realMeanR > r.MeanR!.Value ? 1 : 0) : null, comparable[i], comparable[i] ? null : NotComparable(r, minPlaced))).ToList();
        var reason = string.Create(c, $"the real mean {realMeanR:0.####} R ranks {rank} of {k + 1} ({atLeast} placebos at or above it), p = {p:0.####} against {alpha:0.####}");
        return new PlaceboSetResult(readings.Count, k, options.MinPlacebos, realMeanR, rank, p, alpha, pass ? NullStatus.Pass : NullStatus.Fail, reason, rows);
    }

    private static string NotComparable(PlaceboReading r, int minPlaced) =>
        r.Placed == 0 ? r.NotPlacedReason ?? "no trade could be placed" : "only " + r.Placed.ToString(CultureInfo.InvariantCulture) + " trades could be placed (at least " + minPlaced.ToString(CultureInfo.InvariantCulture) + " are needed)";
}
