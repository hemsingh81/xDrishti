using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Market.Strength;

/// <summary>Index-relative strength (BK-09 <c>rs_vs_index</c>): the share's n-session return minus the index's, in percentage points, both measured on the index's sessions ending on the day. Never 0 for a missing input. Pure.</summary>
public static class IndexRelative
{
    public static Metric Compute(IReadOnlyList<DailyClose> share, IReadOnlyList<DailyClose> index, DateOnly asOf, int window)
    {
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(index);
        var (iDates, iCloses, _) = BasketStats.Clean(index, DateOnly.MinValue, asOf);
        if (iDates.Length == 0 || iDates[^1] != asOf)
        {
            return Metric.Unavailable("no index bar on the day");
        }

        if (iDates.Length < window + 1)
        {
            return Metric.Unavailable($"index has {iDates.Length} closes, {window + 1} needed");
        }

        var from = iDates[^(window + 1)];
        var (sDates, sCloses, _) = BasketStats.Clean(share, from, asOf);
        if (sDates.Length == 0 || sDates[0] != from || sDates[^1] != asOf)
        {
            return Metric.Unavailable($"the share lacks a close on {from:yyyy-MM-dd} or {asOf:yyyy-MM-dd}");
        }

        var sr = (sCloses[^1] / sCloses[0]) - 1.0;
        var ir = (iCloses[^1] / iCloses[^(window + 1)]) - 1.0;
        return Metric.Of(100.0 * (sr - ir));
    }
}
