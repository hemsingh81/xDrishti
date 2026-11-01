namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Criterion 7, the stitching of one rolling series on one session: missing bars are counted; a strike move of more than <see cref="OptionQualityThresholds.StrikeJumpSteps"/> strike steps between consecutive bars is a <c>strike_jump</c>;
/// the bar's spot is compared with the underlying's 1-minute close at the bar's end minute and a difference above <see cref="OptionQualityThresholds.SpotTolerance"/> is counted (no 1-minute bar: that comparison is skipped and the
/// reason says so). Any strike jump warns, more than <see cref="OptionQualityThresholds.StitchFailShare"/> of the bars fails; a spot mismatch above <see cref="OptionQualityThresholds.SpotWarnShare"/> of the compared bars warns. An unknown strike step is
/// <c>unavailable/no_strike_step</c>; fewer than two bars, <c>unavailable/too_few_points</c>.
/// </summary>
public static class StitchingCheck
{
    public static QualityCheck Evaluate(IReadOnlyList<RollingPoint> points, int expectedBars, int? strikeStepPaise, OptionQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(thresholds);
        var missing = Math.Max(0, expectedBars - points.Count);
        if (points.Count < 2)
        {
            return QualityCheck.Unavailable(QualityCodes.Stitching, QualityCodes.TooFewPoints, points.Count, QualityDetail.Counts(("missing_bars", missing)));
        }

        var ordered = points.OrderBy(p => p.Ts).ToList();
        long jumps = 0;
        if (strikeStepPaise is > 0)
        {
            for (var i = 1; i < ordered.Count; i++)
            {
                if (Math.Abs((long)ordered[i].StrikePx - ordered[i - 1].StrikePx) > (long)thresholds.StrikeJumpSteps * strikeStepPaise.Value)
                {
                    jumps++;
                }
            }
        }

        long compared = 0;
        long mismatched = 0;
        foreach (var p in ordered.Where(p => p.UnderlyingClosePx is > 0))
        {
            compared++;
            if (Math.Abs((double)p.SpotPx - p.UnderlyingClosePx!.Value) / p.UnderlyingClosePx.Value > thresholds.SpotTolerance)
            {
                mismatched++;
            }
        }

        var detail = QualityDetail.Counts(("missing_bars", missing), ("strike_jumps", jumps), ("spot_compared", compared), ("spot_mismatches", mismatched));
        if (strikeStepPaise is not > 0)
        {
            return QualityCheck.Unavailable(QualityCodes.Stitching, QualityCodes.NoStrikeStep, ordered.Count, detail);
        }

        var share = (double)jumps / ordered.Count;
        var spotShare = compared == 0 ? 0 : (double)mismatched / compared;
        var status = share > thresholds.StitchFailShare ? QualityStatus.Fail : jumps > 0 || spotShare > thresholds.SpotWarnShare ? QualityStatus.Warn : QualityStatus.Pass;
        return new QualityCheck(QualityCodes.Stitching, status, share, thresholds.StitchFailShare, compared == 0 ? QualityCodes.NoUnderlyingBar : null, ordered.Count, detail);
    }
}
