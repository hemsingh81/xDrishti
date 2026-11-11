using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// Breadth inside a basket on one day (BK-06 m9): the share of voters above their 20, 50 and 200 day average (the voter rule of <see cref="BreadthCalculator"/>: a complete bar on each session of the average, no corporate-action data is read here), the advance share and the share at a 252-session high. A member without enough history is excluded from that figure only, named and counted.
/// The same function serves any member set, so the tracked-set breadth of P3-01e is this one called with another set. Pure.
/// </summary>
public static class BasketBreadth
{
    /// <summary>The reason a member is left out of an average or high figure.</summary>
    public const string ShortHistory = "short_history";

    /// <summary>The reason a member is left out of every figure: it has no bar on the day.</summary>
    public const string NoBar = "no_bar";

    private const int HighSessions = 252;

    private static readonly int[] SmaWindows = [20, 50, 200];

    private static readonly Dictionary<Guid, IReadOnlySet<DateOnly>> NoExDates = [];

    public static BasketBreadthResult Compute(IReadOnlyList<MemberSeries> members, DateOnly asOf, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        var calendar = Calendar.Build(members, asOf);
        var figures = new BreadthFigure[SmaWindows.Length];
        for (var k = 0; k < SmaWindows.Length; k++)
        {
            figures[k] = Average(calendar, asOf, SmaWindows[k], options, true);
        }

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
            figures[0],
            figures[1],
            figures[2],
            AdvanceDecline(adv, dec, unch, options.MinVoters),
            Share(highCount, highVoters, options.MinVoters, highExcluded),
            noBar);
    }

    /// <summary>The share above the 20-day average on each of the last <paramref name="days"/> sessions up to <paramref name="asOf"/>, oldest first; a day without enough voters is insufficient.</summary>
    public static IReadOnlyList<BreadthPoint> SeriesAbove20(IReadOnlyList<MemberSeries> members, DateOnly asOf, int days, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        var calendar = Calendar.Build(members, asOf);
        var from = Math.Max(0, calendar.Sessions.Length - Math.Max(0, days));
        var points = new List<BreadthPoint>(calendar.Sessions.Length - from);
        for (var i = from; i < calendar.Sessions.Length; i++)
        {
            points.Add(new BreadthPoint(calendar.Sessions[i], Average(calendar, calendar.Sessions[i], 20, options, false).Share));
        }

        return points;
    }

    /// <summary>The share of voters above their k-session average on a day: the voter rule of P3-01e (<see cref="BreadthCalculator"/>), called with the basket's members and the sessions any of them traded.</summary>
    private static BreadthFigure Average(Calendar calendar, DateOnly day, int sessions, BasketProfileOptions options, bool nameExcluded)
    {
        var broad = new BreadthOptions(sessions, 1, 0.0, 0.0);
        var result = BreadthCalculator.Compute(day, calendar.Sessions, calendar.Inputs, NoExDates, broad);
        var excluded = new List<string>();
        for (var i = 0; i < calendar.Inputs.Count; i++)
        {
            // a member with a bar on the day that does not vote for lack of a complete window
            if (nameExcluded && calendar.Inputs[i].Closes.ContainsKey(day) && BreadthCalculator.Compute(day, calendar.Sessions, [calendar.Inputs[i]], NoExDates, broad).Voters == 0)
            {
                excluded.Add(calendar.Symbols[i]);
            }
        }

        var share = result.Voters < options.MinVoters || result.Value is not { } percent ? BasketStats.Short(result.Voters, options.MinVoters) : Metric.Of(percent / 100.0);
        return new BreadthFigure(share, result.Voters, excluded, ShortHistory);
    }

    private sealed record Calendar(DateOnly[] Sessions, IReadOnlyList<BreadthInput> Inputs, IReadOnlyList<string> Symbols)
    {
        public static Calendar Build(IReadOnlyList<MemberSeries> members, DateOnly asOf)
        {
            var ordered = members.OrderBy(m => m.Symbol, StringComparer.Ordinal).ToList();
            var inputs = new List<BreadthInput>(ordered.Count);
            var all = new SortedSet<DateOnly>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var (dates, closes, _) = BasketStats.Clean(ordered[i].Closes, DateOnly.MinValue, asOf);
                var map = new Dictionary<DateOnly, double>(dates.Length);
                for (var d = 0; d < dates.Length; d++)
                {
                    map[dates[d]] = closes[d];
                    all.Add(dates[d]);
                }

                inputs.Add(new BreadthInput(new Guid(i, 0, 0, new byte[8]), map));
            }

            return new Calendar([.. all], inputs, [.. ordered.Select(m => m.Symbol)]);
        }
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
