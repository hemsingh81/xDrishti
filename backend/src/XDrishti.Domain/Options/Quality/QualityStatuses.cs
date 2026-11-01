namespace XDrishti.Domain.Options.Quality;

/// <summary>The stored words of a <see cref="QualityStatus"/> and the roll-up of several: <c>fail</c> above <c>unavailable</c> above <c>warn</c> above <c>pass</c>; nothing to roll up is <c>unavailable</c>, never <c>pass</c>.</summary>
public static class QualityStatuses
{
    public static string Code(this QualityStatus status) => status switch
    {
        QualityStatus.Pass => "pass",
        QualityStatus.Warn => "warn",
        QualityStatus.Fail => "fail",
        _ => "unavailable",
    };

    public static bool TryParse(string? code, out QualityStatus status)
    {
        status = QualityStatus.Unavailable;
        switch (code)
        {
            case "pass":
                status = QualityStatus.Pass;
                return true;
            case "warn":
                status = QualityStatus.Warn;
                return true;
            case "fail":
                status = QualityStatus.Fail;
                return true;
            case "unavailable":
                return true;
            default:
                return false;
        }
    }

    /// <summary>The worst of the statuses (fail, unavailable, warn, pass); an empty list is unavailable.</summary>
    public static QualityStatus Worst(IEnumerable<QualityStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        var any = false;
        var worst = QualityStatus.Pass;
        foreach (var status in statuses)
        {
            any = true;
            if (Rank(status) > Rank(worst))
            {
                worst = status;
            }
        }

        return any ? worst : QualityStatus.Unavailable;
    }

    private static int Rank(QualityStatus status) => status switch
    {
        QualityStatus.Pass => 0,
        QualityStatus.Warn => 1,
        QualityStatus.Unavailable => 2,
        _ => 3,
    };
}
