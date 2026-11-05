using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The learning-curve verdict of spec CF-12, D5 (ADR 0218), over gains that are given: the refit loop that produces them is not part of this slice. A straight line is fitted by ordinary least squares to the last 3 to 5 points. With 4 or more points the curve is
/// <b>rising</b> if <c>slope - 1.645 SE &gt; 0</c> and <b>flat</b> if <c>slope + 1.645 SE &lt; min_slope</c> (per 50 rows, scaled to rows), else <b>unclear</b>; with 3 points there is no standard error and it is unclear. When the slope is above zero the rows at which the
/// line reaches the target gain are projected (a statement only when the curve is rising). "Flat" means more data will not help this recipe. Pure.
/// </summary>
public static class LearningCurve
{
    private const double Z = 1.645;

    /// <summary>Reads the curve.</summary>
    public static CurveResult Evaluate(IReadOnlyList<LearningCurvePoint> points, LoopOptions options)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Problems().Count > 0)
        {
            throw new ArgumentException("The loop options are out of range: " + string.Join("; ", options.Problems()), nameof(options));
        }

        var ordered = points.OrderBy(p => p.Rows).ToList();
        if (ordered.Any(p => !double.IsFinite(p.Gain) || p.Rows < 1) || ordered.Select(p => p.Rows).Distinct().Count() != ordered.Count)
        {
            return new CurveResult(CurveVerdict.Insufficient, 0, null, null, null, null, null, "unavailable: a point has a gain that is not a number or the rows repeat");
        }

        if (ordered.Count < 3)
        {
            return new CurveResult(CurveVerdict.Insufficient, ordered.Count, null, null, null, null, null, string.Create(CultureInfo.InvariantCulture, $"unavailable: {ordered.Count} point(s), three are needed"));
        }

        var used = ordered.Skip(Math.Max(0, ordered.Count - 5)).ToList();
        var k = used.Count;
        var xMean = used.Average(p => (double)p.Rows);
        var yMean = used.Average(p => p.Gain);
        double sxx = 0, sxy = 0, syy = 0;
        foreach (var p in used)
        {
            var dx = p.Rows - xMean;
            var dy = p.Gain - yMean;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        var slope = sxy / sxx;
        double? se = null;
        if (k >= 4)
        {
            var sse = Math.Max(0, syy - (slope * sxy));
            se = Math.Sqrt(sse / (k - 2) / sxx);
        }

        var verdict = CurveVerdict.Unclear;
        var minPerRow = options.MinSlopePer50Rows / 50;
        if (se is { } s)
        {
            if (slope - (Z * s) > 0)
            {
                verdict = CurveVerdict.Rising;
            }
            else if (slope + (Z * s) < minPerRow)
            {
                verdict = CurveVerdict.Flat;
            }
        }

        double? projected = null, more = null;
        if (slope > 0)
        {
            var last = used[^1].Rows;
            projected = xMean + ((options.TargetGain - yMean) / slope);
            more = Math.Max(0, projected.Value - last);
            projected = Math.Max(projected.Value, last);
        }

        var ci = CultureInfo.InvariantCulture;
        var text = string.Create(ci, $"learning curve {verdict.ToString().ToLowerInvariant()}: slope {slope * 50:F5} per 50 rows over the last {k} points")
            + (se is { } e ? string.Create(ci, $" (standard error {e * 50:F5} per 50 rows)") : " (three points give no standard error)")
            + (verdict == CurveVerdict.Flat ? "; more data will not help this recipe" : string.Empty)
            + (projected is { } p2 ? string.Create(ci, $"; the line reaches a gain of {options.TargetGain:F3} at about {p2:F0} rows, {more!.Value:F0} more") + (verdict == CurveVerdict.Rising ? string.Empty : " (not a finding: the curve is not shown to be rising)") : string.Empty);
        return new CurveResult(verdict, k, slope, slope * 50, se, projected, more, text);
    }
}
