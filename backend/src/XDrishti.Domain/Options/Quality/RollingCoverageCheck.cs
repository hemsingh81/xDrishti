namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Criterion 3: the stored bars of one fetch key on one session over the bars a session has at the interval (75 at 5 minutes, 25 at 15). A key with no bar whose fetch log ended <c>empty</c> or <c>no_data</c> inside a known out-of-era period is
/// <c>unavailable/out_of_era</c>; outside such a period it is a fail like any other missing data.
/// </summary>
public static class RollingCoverageCheck
{
    /// <summary>The session length in minutes (09:15 to 15:30).</summary>
    public const int SessionMinutes = 375;

    /// <summary>The bars a regular session has at the interval (the first bar starts at 09:15).</summary>
    public static int ExpectedBars(int intervalMinutes) => intervalMinutes <= 0 ? 0 : (SessionMinutes + intervalMinutes - 1) / intervalMinutes;

    public static QualityCheck Evaluate(int stored, int intervalMinutes, string? fetchOutcome, bool outOfEra, OptionQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var expected = ExpectedBars(intervalMinutes);
        if (expected == 0)
        {
            return QualityCheck.Unavailable(QualityCodes.Coverage, QualityCodes.NotRecorded);
        }

        if (stored <= 0 && outOfEra && fetchOutcome is "empty" or "no_data")
        {
            return QualityCheck.Unavailable(QualityCodes.Coverage, QualityCodes.OutOfEra, expected);
        }

        var detail = QualityDetail.Counts(("stored", stored), ("expected", expected));
        return CoverageJudge.Judge(QualityCodes.Coverage, Math.Min(stored, expected), expected, thresholds, detail);
    }
}
