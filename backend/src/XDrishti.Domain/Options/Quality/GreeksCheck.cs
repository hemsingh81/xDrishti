namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Criterion 5, IV and Greeks sanity of a recorded chain, among the non-null values: <c>0 &lt; iv &lt; MaxIv</c>; call delta in [0, 1] and put delta in [-1, 0]; call and put delta non-increasing in strike (tolerance <see cref="OptionQualityThresholds.DeltaTolerance"/>);
/// gamma and vega not negative; across the expiries of one snapshot the at-the-money total variance <c>iv^2 T</c> not falling (tolerance <see cref="OptionQualityThresholds.CalendarTolerance"/>). A row flagged <c>bad_greeks</c> is one point and one violation. Violations in more
/// than <see cref="OptionQualityThresholds.GreeksWarn"/> of the points warn, in more than <see cref="OptionQualityThresholds.GreeksFail"/> fail; fewer than <see cref="OptionQualityThresholds.MinPoints"/> points are <c>unavailable/too_few_points</c>.
/// </summary>
public static class GreeksCheck
{
    public static QualityCheck Evaluate(IReadOnlyList<ChainPoint> points, DateOnly sessionDate, OptionQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(thresholds);
        long n = 0;
        long bad = 0;
        long ranges = 0;
        long order = 0;
        long calendar = 0;

        void Point(bool violation, ref long kind)
        {
            n++;
            if (violation)
            {
                bad++;
                kind++;
            }
        }

        foreach (var p in points)
        {
            if (p.BadGreeks)
            {
                Point(true, ref ranges);
                continue;
            }

            if (p.Iv is { } iv)
            {
                Point(!(iv > 0 && iv < thresholds.MaxIv), ref ranges);
            }

            if (p.Delta is { } delta)
            {
                Point(p.IsCall ? delta is < 0 or > 1 : delta is < -1 or > 0, ref ranges);
            }

            if (p.Gamma is { } gamma)
            {
                Point(gamma < 0, ref ranges);
            }

            if (p.Vega is { } vega)
            {
                Point(vega < 0, ref ranges);
            }
        }

        foreach (var side in points.Where(p => p.Delta is not null && !p.BadGreeks).GroupBy(p => (p.Ts, p.Expiry, p.IsCall)))
        {
            ChainPoint? previous = null;
            foreach (var p in side.OrderBy(x => x.StrikePx))
            {
                if (previous is not null)
                {
                    Point(p.Delta!.Value > previous.Delta!.Value + thresholds.DeltaTolerance, ref order);
                }

                previous = p;
            }
        }

        foreach (var snapshot in points.Where(p => p.IsCall && p.Iv is > 0 && !p.BadGreeks && p.Expiry > sessionDate).GroupBy(p => p.Ts))
        {
            var atm = snapshot.GroupBy(p => p.Expiry).OrderBy(g => g.Key)
                .Select(g => (Expiry: g.Key, Point: g.MinBy(p => Math.Abs((long)p.StrikePx - p.UnderlyingPx))!))
                .Select(x => x.Point.Iv!.Value * x.Point.Iv!.Value * (x.Expiry.DayNumber - sessionDate.DayNumber) / 365.0)
                .ToList();
            for (var i = 1; i < atm.Count; i++)
            {
                Point(atm[i] < atm[i - 1] * (1 - thresholds.CalendarTolerance), ref calendar);
            }
        }

        var detail = QualityDetail.Counts(("range_violations", ranges), ("delta_order_violations", order), ("calendar_violations", calendar));
        if (n < thresholds.MinPoints)
        {
            return QualityCheck.Unavailable(QualityCodes.Greeks, QualityCodes.TooFewPoints, (int)n, detail);
        }

        var share = (double)bad / n;
        var status = share > thresholds.GreeksFail ? QualityStatus.Fail : share > thresholds.GreeksWarn ? QualityStatus.Warn : QualityStatus.Pass;
        return new QualityCheck(QualityCodes.Greeks, status, share, thresholds.GreeksWarn, null, (int)Math.Min(n, int.MaxValue), detail);
    }
}
