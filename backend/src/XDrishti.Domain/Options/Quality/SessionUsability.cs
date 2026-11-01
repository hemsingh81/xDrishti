namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Criterion 8, when a session counts: a chain session of an underlying is <b>usable</b> when its coverage passes and its parity passes or warns; a rolling key-session when its coverage passes and its stitching passes or warns. A missing check, a failed
/// one and an <c>unavailable</c> one all make it unusable (an unavailable check never counts as a pass; this is the stricter reading of "not a fail", ADR 0178).
/// </summary>
public static class SessionUsability
{
    public static bool IsUsable(string source, IReadOnlyCollection<QualityCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var second = string.Equals(source, QualityCodes.Chain, StringComparison.Ordinal) ? QualityCodes.Parity : QualityCodes.Stitching;
        var coverage = checks.FirstOrDefault(c => c.Code == QualityCodes.Coverage);
        var other = checks.FirstOrDefault(c => c.Code == second);
        return coverage is { Status: QualityStatus.Pass } && other is { Status: QualityStatus.Pass or QualityStatus.Warn };
    }
}
