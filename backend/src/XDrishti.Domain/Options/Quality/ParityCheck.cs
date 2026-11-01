namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Criterion 4, put-call parity of a recorded chain (European style, no early-exercise term). For each snapshot and expiry: the strikes where both sides have a bid and an ask (fewer than <see cref="OptionQualityThresholds.MinPoints"/> are skipped);
/// the forward from the strike nearest the money, <c>F = K0 + (Cmid - Pmid) / DF</c>, <c>DF = exp(-r T)</c>; at every other strike <c>res = |(Cmid - Pmid) - DF (F - K)|</c> against <c>tol = (Cask - Cbid + Pask - Pbid) / 2 + 1 tick</c>.
/// The strike K0 is not tested (its residual is zero by construction). The session's value is the share of tested strikes with <c>res &lt;= tol</c>, pooled over all snapshots and expiries: pass from <see cref="OptionQualityThresholds.ParityPass"/>, warn from
/// <see cref="OptionQualityThresholds.ParityWarn"/>, else fail. Nothing testable is <c>unavailable</c>.
/// </summary>
public static class ParityCheck
{
    public static QualityCheck Evaluate(IReadOnlyList<ChainPoint> points, DateOnly sessionDate, OptionQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(thresholds);
        long tested = 0;
        long passed = 0;
        long groupsUsed = 0;
        long groupsSkipped = 0;
        long crossed = 0;
        var anyTwoSided = false;
        foreach (var group in points.GroupBy(p => (p.Ts, p.Expiry)))
        {
            crossed += group.Count(p => p.Crossed);
            var calls = group.Where(p => p.IsCall && p.TwoSided).GroupBy(p => p.StrikePx).ToDictionary(g => g.Key, g => g.First());
            var puts = group.Where(p => !p.IsCall && p.TwoSided).GroupBy(p => p.StrikePx).ToDictionary(g => g.Key, g => g.First());
            var strikes = calls.Keys.Where(puts.ContainsKey).Order().ToList();
            if (strikes.Count == 0)
            {
                continue;
            }

            anyTwoSided = true;
            if (strikes.Count < thresholds.MinPoints)
            {
                groupsSkipped++;
                continue;
            }

            var spot = group.First().UnderlyingPx;
            var days = Math.Max(0, group.Key.Expiry.DayNumber - sessionDate.DayNumber);
            var df = Math.Exp(-thresholds.Rate * days / 365.0);
            var k0 = strikes.MinBy(k => Math.Abs((long)k - spot));
            var diff0 = Mid(calls[k0]) - Mid(puts[k0]);
            var forward = k0 + diff0 / df;
            groupsUsed++;
            foreach (var k in strikes.Where(k => k != k0))
            {
                var call = calls[k];
                var put = puts[k];
                var residual = Math.Abs(Mid(call) - Mid(put) - df * (forward - k));
                var tolerance = (call.AskPx!.Value - call.BidPx!.Value + put.AskPx!.Value - put.BidPx!.Value) / 2.0 + thresholds.TickPaise;
                tested++;
                if (residual <= tolerance)
                {
                    passed++;
                }
            }
        }

        var detail = QualityDetail.Counts(("snapshots_tested", groupsUsed), ("snapshots_skipped", groupsSkipped), ("crossed_quotes", crossed));
        if (tested == 0)
        {
            return QualityCheck.Unavailable(QualityCodes.Parity, anyTwoSided ? QualityCodes.TooFewPoints : QualityCodes.NoTwoSidedQuote, 0, detail);
        }

        var share = (double)passed / tested;
        var status = share >= thresholds.ParityPass ? QualityStatus.Pass : share >= thresholds.ParityWarn ? QualityStatus.Warn : QualityStatus.Fail;
        return new QualityCheck(QualityCodes.Parity, status, share, thresholds.ParityPass, null, (int)Math.Min(tested, int.MaxValue), detail);
    }

    private static double Mid(ChainPoint p) => (p.BidPx!.Value + p.AskPx!.Value) / 2.0;
}
