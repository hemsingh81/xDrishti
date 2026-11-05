using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Decision-quality lift (spec CF-12, D7; ADR 0218): within each night the candidates with a stored score are ranked by it (ties by key); the top half is the first <c>m / 2</c> and the bottom half the last <c>m / 2</c> (the middle one of an odd night is in neither,
/// and a night with fewer than two scored candidates is left out). It reports the mean R of the top half against the bottom half with the standard error, and the share of the owner's approved tickets that were in the top half against 50 % by chance. A figure
/// is called not significant below 1.96 standard errors. Days are not resampled here (a plain standard error), so the result is a description and not a gate. Pure.
/// </summary>
public static class DecisionLift
{
    private const double Significant = 1.96;

    /// <summary>The lift and the approval share over the candidates.</summary>
    public static LiftResult Compute(IReadOnlyList<LiftCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var top = new List<double>();
        var bottom = new List<double>();
        int approvedTop = 0, approvedBottom = 0;
        foreach (var night in candidates.Where(c => c.Score is { } s && double.IsFinite(s)).GroupBy(c => c.Day))
        {
            var ranked = night.OrderByDescending(c => c.Score!.Value).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
            var half = ranked.Count / 2;
            for (var i = 0; i < half; i++)
            {
                Add(ranked[i], top, ref approvedTop);
                Add(ranked[ranked.Count - 1 - i], bottom, ref approvedBottom);
            }
        }

        double? topMean = top.Count == 0 ? null : top.Average();
        double? bottomMean = bottom.Count == 0 ? null : bottom.Average();
        double? lift = null, se = null, z = null;
        if (top.Count >= 2 && bottom.Count >= 2)
        {
            var pooled = PooledSd(top, bottom);
            (lift, se) = LiftFrom(top.Count, topMean!.Value, bottom.Count, bottomMean!.Value, pooled);
            z = se is > 0 ? lift / se : null;
            if (z is null)
            {
                lift = se = null;
            }
        }

        var tickets = approvedTop + approvedBottom;
        double? share = tickets == 0 ? null : (double)approvedTop / tickets;
        double? shareZ = tickets == 0 ? null : ShareZ(approvedTop, tickets);
        var ci = CultureInfo.InvariantCulture;
        var text = (lift is { } l
                ? string.Create(ci, $"top half n={top.Count} mean {topMean:F4} R, bottom half n={bottom.Count} mean {bottomMean:F4} R: lift {l:F4} R, standard error {se:F4}, z {z:F2}, {(Math.Abs(z!.Value) >= Significant ? "significant" : "not significant")}")
                : "lift unavailable: each half needs two graded trades with a stored score and a spread")
            + "; "
            + (share is { } sh
                ? string.Create(ci, $"{approvedTop} of {tickets} approved tickets in the top half = {sh * 100:F0} % against 50 % by chance, z {shareZ:F2}, {(Math.Abs(shareZ!.Value) >= Significant ? "significant" : "not significant")}")
                : "no approved ticket had a stored score");
        return new LiftResult(top.Count, bottom.Count, topMean, bottomMean, lift, se, z, tickets, approvedTop, share, shareZ, text);
    }

    /// <summary>The lift and its standard error from summary figures: <c>mean1 - mean2</c> and <c>sd * sqrt(1/n1 + 1/n2)</c>.</summary>
    public static (double Lift, double StandardError) LiftFrom(int n1, double mean1, int n2, double mean2, double pooledSd) =>
        (mean1 - mean2, pooledSd * Math.Sqrt((1.0 / n1) + (1.0 / n2)));

    /// <summary>The z of <paramref name="inTop"/> of <paramref name="tickets"/> against 50 %: <c>(share - 0.5) / sqrt(0.25 / tickets)</c>.</summary>
    public static double ShareZ(int inTop, int tickets) => (((double)inTop / tickets) - 0.5) / Math.Sqrt(0.25 / tickets);

    private static void Add(LiftCandidate c, List<double> halfR, ref int approved)
    {
        if (c.Approved)
        {
            approved++;
        }

        if (c.R is { } r && double.IsFinite(r))
        {
            halfR.Add(r);
        }
    }

    private static double PooledSd(List<double> a, List<double> b)
    {
        var ma = a.Average();
        var mb = b.Average();
        var ss = a.Sum(v => (v - ma) * (v - ma)) + b.Sum(v => (v - mb) * (v - mb));
        return Math.Sqrt(ss / (a.Count + b.Count - 2));
    }
}
