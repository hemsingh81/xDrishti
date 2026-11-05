using System.Globalization;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The mirror gate (spec CF-07, D3): the mirror (the opposite side with the stop and target mirrored, same costs) must have a net expectancy of at most 0. A positive mirror means the "edge" is drift or beta and not timing, and reads "mostly drift".
/// Unavailable (too few trades placed, or a value that is not finite) is never a pass. Pure.
/// </summary>
public static class MirrorGate
{
    /// <summary>The level the mirror must not exceed.</summary>
    public const double Threshold = 0;

    /// <param name="realMeanR">Mean net R of the real trades.</param>
    /// <param name="mirrorMeanR">Mean net R of the mirror trades; null when none.</param>
    /// <param name="placed">How many mirror trades that mean is over.</param>
    /// <param name="minPlaced">The fewest mirror trades that make the reading usable.</param>
    public static MirrorResult Evaluate(double realMeanR, double? mirrorMeanR, int placed, int minPlaced)
    {
        var c = CultureInfo.InvariantCulture;
        if (mirrorMeanR is not { } mean || !double.IsFinite(mean) || !double.IsFinite(realMeanR) || placed < minPlaced)
        {
            return new MirrorResult(placed, null, realMeanR, Threshold, NullStatus.Unavailable, "unavailable: " + placed.ToString(c) + " mirror trades could be placed (at least " + minPlaced.ToString(c) + " are needed)");
        }

        return mean <= Threshold
            ? new MirrorResult(placed, mean, realMeanR, Threshold, NullStatus.Pass, string.Create(c, $"the mirror earns {mean:0.####} R, at most 0, against {realMeanR:0.####} R for the strategy"))
            : new MirrorResult(placed, mean, realMeanR, Threshold, NullStatus.Fail, string.Create(c, $"mostly drift: the mirror earns {mean:0.####} R, above 0, against {realMeanR:0.####} R for the strategy"));
    }
}
