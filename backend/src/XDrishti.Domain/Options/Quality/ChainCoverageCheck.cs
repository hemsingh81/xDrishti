namespace XDrishti.Domain.Options.Quality;

/// <summary>Criterion 2: the requests that ended <c>ok</c> over the planned requests of one underlying on one session. A day with no session row, or no request for the underlying, is <c>unavailable/not_recorded</c>, never a fail.</summary>
public static class ChainCoverageCheck
{
    public static QualityCheck Evaluate(bool sessionRecorded, int planned, int ok, IReadOnlyDictionary<string, int> outcomes, OptionQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (!sessionRecorded || planned <= 0)
        {
            return QualityCheck.Unavailable(QualityCodes.Coverage, QualityCodes.NotRecorded);
        }

        // Gaps by outcome: every outcome word with its count (ok included, so the sum is the planned count).
        var detail = QualityDetail.Counts([.. outcomes.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => (o.Key, (long)o.Value))]);
        return CoverageJudge.Judge(QualityCodes.Coverage, Math.Min(ok, planned), planned, thresholds, detail);
    }
}
