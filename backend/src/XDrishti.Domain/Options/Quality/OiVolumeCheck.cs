namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Criterion 6, between consecutive snapshots of one contract on one session: cumulative volume must not fall (any fall is a fail); an open-interest change above <see cref="OptionQualityThresholds.OiJumpPct"/> percent and above
/// <see cref="OptionQualityThresholds.OiJumpMinLots"/> lots is counted as a jump (the lot size unknown: percent only), and more than <see cref="OptionQualityThresholds.OiWarnShare"/> of the pairs warns. A jump alone is never a fail (block trades exist). A pair
/// whose earlier open interest is zero has no percent change and is not a jump. No pair at all is <c>unavailable/too_few_points</c>.
/// </summary>
public static class OiVolumeCheck
{
    public static QualityCheck Evaluate(IReadOnlyList<ChainPoint> points, int? lotSize, OptionQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(thresholds);
        long pairs = 0;
        long falls = 0;
        long jumps = 0;
        foreach (var contract in points.GroupBy(p => p.SecurityId))
        {
            ChainPoint? previous = null;
            foreach (var p in contract.OrderBy(x => x.Ts))
            {
                if (previous is not null)
                {
                    pairs++;
                    if (previous.Volume is { } before && p.Volume is { } after && after < before)
                    {
                        falls++;
                    }

                    if (previous.Oi is > 0 and { } oldOi && p.Oi is { } newOi)
                    {
                        var change = Math.Abs(newOi - oldOi);
                        if (change * 100.0 / oldOi > thresholds.OiJumpPct && (lotSize is not > 0 || change > (long)thresholds.OiJumpMinLots * lotSize.Value))
                        {
                            jumps++;
                        }
                    }
                }

                previous = p;
            }
        }

        var detail = QualityDetail.Counts(("volume_falls", falls), ("oi_jumps", jumps));
        if (pairs == 0)
        {
            return QualityCheck.Unavailable(QualityCodes.OiVolume, QualityCodes.TooFewPoints, 0, detail);
        }

        var share = (double)jumps / pairs;
        var status = falls > 0 ? QualityStatus.Fail : share > thresholds.OiWarnShare ? QualityStatus.Warn : QualityStatus.Pass;
        return new QualityCheck(QualityCodes.OiVolume, status, share, thresholds.OiWarnShare, null, (int)Math.Min(pairs, int.MaxValue), detail);
    }
}
