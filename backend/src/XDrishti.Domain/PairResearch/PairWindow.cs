namespace XDrishti.Domain.PairResearch;

/// <summary>
/// The formation window of a calendar quarter (P14-13), as a pure function of the daily series: only points dated <b>before</b> the quarter starts are looked at (a later point, or an instrument whose first bar is later, changes nothing). A session is a date on which at least half of the
/// instruments that have any bar before the quarter have one; the window is the last <c>FormationSessions</c> of them; a member needs a close on every session of the window (else it is left out and counted); its turnover is the mean of the last 20 sessions. Members are in symbol order.
/// </summary>
public static class PairWindow
{
    private const int TurnoverSessions = 20;

    public static PairWindowResult Build(IReadOnlyList<DailySeries> series, DateOnly quarterStart, PairStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        var usable = series.Select(s => (s.Symbol, s.Sector, Days: s.Points.Where(p => p.Date < quarterStart && p.Close > 0 && double.IsFinite(p.Close)).GroupBy(p => p.Date).ToDictionary(g => g.Key, g => g.Last())))
            .Where(s => s.Days.Count > 0).OrderBy(s => s.Symbol, StringComparer.Ordinal).ToList();
        var coverage = new Dictionary<DateOnly, int>();
        foreach (var s in usable)
        {
            foreach (var date in s.Days.Keys)
            {
                coverage[date] = coverage.GetValueOrDefault(date) + 1;
            }
        }

        var sessions = coverage.Where(kv => kv.Value * 2 >= usable.Count).Select(kv => kv.Key).Order().ToList();
        if (sessions.Count < options.FormationSessions)
        {
            return new PairWindowResult([], [], 0, usable.Count, sessions.Count);
        }

        var window = sessions.Skip(sessions.Count - options.FormationSessions).ToList();
        var members = new List<PairMember>();
        var incomplete = 0;
        foreach (var s in usable)
        {
            if (window.Any(d => !s.Days.ContainsKey(d)))
            {
                incomplete++;
                continue;
            }

            members.Add(new PairMember(s.Symbol, s.Sector, [.. window.Select(d => s.Days[d].Close)], window.Skip(window.Count - TurnoverSessions).Average(d => s.Days[d].Turnover)));
        }

        return new PairWindowResult(window, members, incomplete, usable.Count, sessions.Count);
    }
}
