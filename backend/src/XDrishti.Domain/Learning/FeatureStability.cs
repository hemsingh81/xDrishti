namespace XDrishti.Domain.Learning;

/// <summary>
/// Whether a feature keeps its direction from fold to fold (CF-10 part 10b, ADR 0215, decision D3). In each fold the direction is the sign of the correlation, over the fold's test rows, between the feature's value and its contribution to the model's margin
/// (+1: a higher value pushes the score up). Across folds the flips are the folds on the minority side; a feature whose direction flips in more than <c>MaxFlipShare</c> (one third) of the folds that give one would be dropped: with 6 folds 3 flips drop it and 2 keep it.
/// A feature whose effect is not monotone (a day of the week) can flip without the model being wrong; the check says "unstable", not "bad". Fewer than two readable folds is <see cref="StabilityVerdict.Unavailable"/>, never stable. Pure.
/// </summary>
public static class FeatureStability
{
    /// <summary>The direction in one fold: -1, 0 or +1. Zero when fewer than <paramref name="minRows"/> rows have a finite value and contribution, or either side does not vary.</summary>
    public static int Direction(IReadOnlyList<double> values, IReadOnlyList<double> contributions, int minRows)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentOutOfRangeException.ThrowIfLessThan(minRows, 2);
        if (values.Count != contributions.Count)
        {
            throw new ArgumentException("values and contributions must have the same length.", nameof(contributions));
        }

        var xs = new List<double>(values.Count);
        var ys = new List<double>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            if (double.IsFinite(values[i]) && double.IsFinite(contributions[i]))
            {
                xs.Add(values[i]);
                ys.Add(contributions[i]);
            }
        }

        if (xs.Count < minRows)
        {
            return 0;
        }

        var mx = xs.Average();
        var my = ys.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < xs.Count; i++)
        {
            sxy += (xs[i] - mx) * (ys[i] - my);
            sxx += (xs[i] - mx) * (xs[i] - mx);
            syy += (ys[i] - my) * (ys[i] - my);
        }

        return sxx <= 1e-12 || syy <= 1e-12 ? 0 : Math.Sign(sxy);
    }

    /// <summary>The verdict on the signs of one feature, one per used fold (0 means no direction that fold).</summary>
    public static FeatureStabilityRow Judge(int index, IReadOnlyList<int> signs, double maxFlipShare)
    {
        ArgumentNullException.ThrowIfNull(signs);
        if (!(maxFlipShare > 0 && maxFlipShare < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(maxFlipShare), "The flip share is above 0 and below 1.");
        }

        var up = signs.Count(s => s > 0);
        var down = signs.Count(s => s < 0);
        var judged = up + down;
        var flips = Math.Min(up, down);
        var verdict = judged < 2 ? StabilityVerdict.Unavailable : flips > (maxFlipShare * judged) + 1e-9 ? StabilityVerdict.Unstable : StabilityVerdict.Stable;
        return new FeatureStabilityRow(index, [.. signs], flips, judged, verdict);
    }
}
