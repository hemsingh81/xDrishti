namespace XDrishti.Domain.Options.Quality;

/// <summary>The one coverage rule of criteria 2 and 3: at least the pass share is pass, at least the warn share is warn, below that fail. A missing share is a gap.</summary>
internal static class CoverageJudge
{
    public static QualityCheck Judge(string code, int have, int planned, OptionQualityThresholds thresholds, string detail)
    {
        var share = (double)have / planned;
        var status = share >= thresholds.CoveragePass ? QualityStatus.Pass : share >= thresholds.CoverageWarn ? QualityStatus.Warn : QualityStatus.Fail;
        return new QualityCheck(code, status, share, thresholds.CoveragePass, status == QualityStatus.Pass ? null : QualityCodes.Gap, planned, detail);
    }
}
