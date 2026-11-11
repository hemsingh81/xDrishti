namespace XDrishti.Domain.Baskets.Derived;

/// <summary>The 1-minute bars of one session (IST date), sorted by time.</summary>
internal sealed record Session(DateOnly Date, IntradayBar[] Bars)
{
    /// <summary>Bars whose minute since the 09:15 open is in [from, to).</summary>
    public IEnumerable<IntradayBar> Between(int from, int to) => Bars.Where(b => IntradaySessions.Minute(b) >= from && IntradaySessions.Minute(b) < to);

    /// <summary>Whether every minute in [from, to) has a bar: a window with a missing minute is not read (a missing minute is never filled forward, BK-03b).</summary>
    public bool IsComplete(int from, int to) => Between(from, to).Select(IntradaySessions.Minute).Distinct().Count() == to - from;
}

/// <summary>Groups intraday bars into sessions using only what was knowable: daily-dated bars up to as_of, plus today's bars strictly before the formation time.</summary>
internal static class IntradaySessions
{
    public static readonly TimeSpan Ist = new(5, 30, 0);
    private static readonly TimeSpan Open = new(9, 15, 0);

    public static int Minute(IntradayBar bar) => (int)(bar.Time.ToOffset(Ist).TimeOfDay - Open).TotalMinutes;

    public static DateOnly DateOf(DateTimeOffset time) => DateOnly.FromDateTime(time.ToOffset(Ist).DateTime);

    /// <summary>The first instant that is not known: the formation time, or midnight after <paramref name="asOf"/>.</summary>
    public static DateTimeOffset Cutoff(DateOnly asOf, DateTimeOffset? formation) =>
        formation ?? new DateTimeOffset(asOf.AddDays(1).ToDateTime(TimeOnly.MinValue), Ist);

    public static List<Session> Build(IntradayBar[]? bars, DateOnly asOf, DateTimeOffset? formation)
    {
        var cutoff = Cutoff(asOf, formation);
        var today = formation is { } f ? DateOf(f) : (DateOnly?)null;
        var groups = new SortedDictionary<DateOnly, List<IntradayBar>>();
        foreach (var bar in bars ?? [])
        {
            var date = DateOf(bar.Time);
            if (bar.Time >= cutoff || (date > asOf && date != today))
            {
                continue;
            }

            if (!groups.TryGetValue(date, out var list))
            {
                groups[date] = list = [];
            }

            list.Add(bar);
        }

        return [.. groups.Select(g => new Session(g.Key, [.. g.Value.OrderBy(b => b.Time)]))];
    }
}
