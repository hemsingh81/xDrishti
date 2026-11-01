namespace XDrishti.Domain.Options.Quality;

/// <summary>One check's result: its code, the status, the measured value against the threshold it was judged by, the reason (a word of <see cref="QualityCodes"/>), the number of points it rests on and a small JSON of counts. Carries no price, IV or Greek.</summary>
public sealed record QualityCheck(string Code, QualityStatus Status, double? Value, double? Threshold, string? Reason, int N, string? Detail)
{
    public static QualityCheck Unavailable(string code, string reason, int n = 0, string? detail = null) => new(code, QualityStatus.Unavailable, null, null, reason, n, detail);
}
