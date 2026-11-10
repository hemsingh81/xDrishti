namespace XDrishti.Domain.Reporting;

/// <summary>
/// The daily-rebalanced equal-weight index (BK-06 m1b, Decision 1 and 2): I(d) = I(d-1) x (1 + mean of r_i(d)), weights from the membership as of the previous session (no look-ahead), an instrument-day with a move above the threshold
/// left out and counted. A day with fewer than the minimum members has no point. Pure.
/// </summary>
public static class EqualWeightIndex
{
    public static IndexSeries Build(IReadOnlyList<MemberSeries> members, Func<DateOnly, IReadOnlySet<string>> membershipAt, ProfileWindow window, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(membershipAt);
        ArgumentNullException.ThrowIfNull(options);
        var ordered = members.OrderBy(m => m.Symbol, StringComparer.Ordinal).ToList();
        var bars = new Dictionary<string, Dictionary<DateOnly, double>>(StringComparer.Ordinal);
        var all = new SortedSet<DateOnly>();
        var dropped = 0;
        foreach (var m in ordered)
        {
            var (dates, closes, drop) = BasketStats.Clean(m.Closes, window.From, window.To);
            dropped += drop;
            var map = new Dictionary<DateOnly, double>(dates.Length);
            for (var i = 0; i < dates.Length; i++)
            {
                map[dates[i]] = closes[i];
                all.Add(dates[i]);
            }

            bars[m.Symbol] = map;
        }

        var timeline = all.ToArray();
        var points = new List<IndexPoint>();
        var leftOut = new List<LeftOutDay>();
        var days = Math.Max(0, timeline.Length - 1);
        var good = 0;
        var lastGood = false;
        var level = 100.0;
        if (timeline.Length > 0)
        {
            points.Add(new IndexPoint(timeline[0], timeline[0], level, 0, 0));
        }

        for (var k = 1; k < timeline.Length; k++)
        {
            var previous = timeline[k - 1];
            var day = timeline[k];
            var set = membershipAt(previous);
            double sum = 0;
            var n = 0;
            foreach (var m in ordered)
            {
                if (!set.Contains(m.Symbol) || !bars[m.Symbol].TryGetValue(previous, out var c0) || !bars[m.Symbol].TryGetValue(day, out var c1))
                {
                    continue;
                }

                var r = (c1 / c0) - 1.0;
                if (Math.Abs(r) > options.DiscontinuityThreshold || !double.IsFinite(r))
                {
                    leftOut.Add(new LeftOutDay(m.Symbol, day, BuyAndHold.Discontinuity));
                    continue;
                }

                sum += r;
                n++;
            }

            lastGood = n >= options.MinMembersForReturn;
            if (!lastGood)
            {
                continue;
            }

            var ret = sum / n;
            level *= 1.0 + ret;
            good++;
            points.Add(new IndexPoint(day, previous, level, ret, n));
        }

        if (days == 0 || good == 0)
        {
            return new IndexSeries(Metric.Empty("no day has enough members"), [], leftOut, dropped, days, good);
        }

        if (!lastGood || good < options.MinIndexCoverage * days)
        {
            return new IndexSeries(Metric.Insufficient($"{good} of {days} days have at least {options.MinMembersForReturn} members, the last day included, and {options.MinIndexCoverage:P0} are needed"), [], leftOut, dropped, days, good);
        }

        return new IndexSeries(Metric.Of(level), points, leftOut, dropped, days, good);
    }
}
