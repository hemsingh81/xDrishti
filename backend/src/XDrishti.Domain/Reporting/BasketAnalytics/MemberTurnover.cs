namespace XDrishti.Domain.Reporting;

/// <summary>Member turnover of a basket over a window (BK-06 m13): (adds + removes) / average members. Needs at least two membership snapshots, otherwise it is unavailable and says from when history exists. Pure.</summary>
public static class MemberTurnover
{
    /// <summary>All membership history is one snapshot, so the basket is constant in time and survivorship biased (ISS-22).</summary>
    public const string SingleSnapshot = "single_snapshot";

    /// <summary>Membership has more than one dated snapshot.</summary>
    public const string Dated = "dated";

    public static TurnoverResult Compute(IReadOnlyList<DateOnly> snapshotDates, Func<DateOnly, IReadOnlySet<string>> membershipAt, ProfileWindow window)
    {
        ArgumentNullException.ThrowIfNull(snapshotDates);
        ArgumentNullException.ThrowIfNull(membershipAt);
        ArgumentNullException.ThrowIfNull(window);
        var dates = snapshotDates.Where(d => d <= window.To).Distinct().Order().ToList();
        if (dates.Count == 0)
        {
            return new TurnoverResult(Metric.Unavailable("no membership snapshot exists"), SingleSnapshot, null, 0, 0);
        }

        if (dates.Count < 2)
        {
            return new TurnoverResult(Metric.Unavailable($"membership history starts on {dates[0]:yyyy-MM-dd}"), SingleSnapshot, dates[0], 0, 0);
        }

        int adds = 0, removes = 0;
        double size = 0;
        var previous = membershipAt(dates[0]);
        size += previous.Count;
        for (var i = 1; i < dates.Count; i++)
        {
            var current = membershipAt(dates[i]);
            size += current.Count;
            if (dates[i] > window.From)
            {
                adds += current.Count(s => !previous.Contains(s));
                removes += previous.Count(s => !current.Contains(s));
            }

            previous = current;
        }

        return new TurnoverResult(FromCounts(adds, removes, size / dates.Count), Dated, dates[0], adds, removes);
    }

    /// <summary>(adds + removes) / average members; not evaluable when the average is zero.</summary>
    public static Metric FromCounts(int adds, int removes, double averageMembers) =>
        averageMembers > 0 ? Metric.Of((adds + removes) / averageMembers) : Metric.NotEvaluable("the basket has no members");
}
