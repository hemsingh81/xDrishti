namespace XDrishti.Domain.Reporting;

/// <summary>How much two baskets share (BK-07 m22): the Jaccard index of their member sets as of one date, |A and B| / |A or B|. It is the honest size of the overlap that the strategy reports only mention in words. Pure.</summary>
public static class MemberOverlap
{
    public static Metric Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var shared = a.Count(b.Contains);
        var union = a.Count + b.Count - shared;
        return union == 0 ? Metric.NotEvaluable("both baskets are empty") : Metric.Of((double)shared / union);
    }
}
