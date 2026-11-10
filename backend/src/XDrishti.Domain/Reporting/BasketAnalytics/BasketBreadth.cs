namespace XDrishti.Domain.Reporting;

/// <summary>
/// Breadth inside a basket on one day (BK-06 m9): the share of voters above their 20, 50 and 200 day average, the advance share and the share at a 252-session high. A member without enough history is excluded from that figure only, named and counted.
/// The same function serves any member set, so the tracked-set breadth of P3-01e is this one called with another set. Pure.
/// </summary>
public static class BasketBreadth
{
    /// <summary>The reason a member is left out of an average or high figure.</summary>
    public const string ShortHistory = "short_history";

    /// <summary>The reason a member is left out of every figure: it has no bar on the day.</summary>
    public const string NoBar = "no_bar";

    private const int HighSessions = 252;

    public static BasketBreadthResult Compute(IReadOnlyList<MemberSeries> members, DateOnly asOf, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        int[] smaWindows = [20, 50, 200];
        var above = new int[3];
        var voters = new int[3];
        var excluded = new List<string>[] { [], [], [] };
        var highVoters = 0;
        var highCount = 0;
        var highExcluded = new List<string>();
        int adv = 0, dec = 0, unch = 0;
        var noBar = new List<string>();
        foreach (var m in members.OrderBy(x => x.Symbol, StringComparer.Ordinal))
        {
            var (dates, closes, _) = BasketStats.Clean(m.Closes, DateOnly.MinValue, asOf);
            if (dates.Length == 0 || dates[^1] != asOf)
            {
                noBar.Add(m.Symbol);
                continue;
            }

            var last = closes[^1];
            for (var k = 0; k < 3; k++)
            {
                if (closes.Length < smaWindows[k])
                {
                    excluded[k].Add(m.Symbol);
                    continue;
                }

                voters[k]++;
                above[k] += last > BasketStats.Mean(closes.AsSpan(closes.Length - smaWindows[k])) ? 1 : 0;
            }

            if (closes.Length >= HighSessions)
            {
                highVoters++;
                highCount += last >= closes.AsSpan(closes.Length - HighSessions).ToArray().Max() ? 1 : 0;
            }
            else
            {
                highExcluded.Add(m.Symbol);
            }

            if (closes.Length >= 2)
            {
                var change = last.CompareTo(closes[^2]);
                adv += change > 0 ? 1 : 0;
                dec += change < 0 ? 1 : 0;
                unch += change == 0 ? 1 : 0;
            }
        }

        return new BasketBreadthResult(
            Share(above[0], voters[0], options.MinVoters, excluded[0]),
            Share(above[1], voters[1], options.MinVoters, excluded[1]),
            Share(above[2], voters[2], options.MinVoters, excluded[2]),
            AdvanceDecline(adv, dec, unch, options.MinVoters),
            Share(highCount, highVoters, options.MinVoters, highExcluded),
            noBar);
    }

    /// <summary>count / voters, or insufficient when there are fewer voters than the minimum.</summary>
    public static BreadthFigure Share(int count, int voters, int minVoters, IReadOnlyList<string> excluded) =>
        new(voters < minVoters ? BasketStats.Short(voters, minVoters) : Metric.Of((double)count / voters), voters, excluded, ShortHistory);

    /// <summary>adv / (adv + dec) over all members that have a prior bar; unchanged members vote but do not count in the share.</summary>
    public static BreadthFigure AdvanceDecline(int advancers, int decliners, int unchanged, int minVoters)
    {
        var voters = advancers + decliners + unchanged;
        var figure = voters < minVoters
            ? BasketStats.Short(voters, minVoters)
            : advancers + decliners == 0 ? Metric.NotEvaluable("no member moved") : Metric.Of((double)advancers / (advancers + decliners));
        return new BreadthFigure(figure, voters, [], ShortHistory);
    }
}
