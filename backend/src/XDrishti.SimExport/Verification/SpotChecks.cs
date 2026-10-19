using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// The spot checks of the verification pack (spec P3-09, V3): the lines of the log that have no card, read from the run records. One <c>excluded_session</c> for each reason that occurs among the session notes (the notes are cut at 100 per
/// instrument, so a reason that has no note is not seen), one <c>flagged_day</c> and one <c>open_at_end</c> trade. Each is chosen by <see cref="SeededRank"/> over its candidates, so the same seed and records give the same lines.
/// </summary>
public static class SpotChecks
{
    /// <summary>The kind of a day the replay left out of the run.</summary>
    public const string ExcludedSession = "excluded_session";

    /// <summary>The kind of a day with a jump the record flags.</summary>
    public const string FlaggedDay = "flagged_day";

    /// <summary>The kind of a trade the data cut short.</summary>
    public const string OpenAtEnd = "open_at_end";

    private static readonly SessionExclusion[] Reasons = [SessionExclusion.SpecialSession, SessionExclusion.ShortSession, SessionExclusion.MissingSession];

    /// <summary>The lines that occur, in the order excluded sessions (special, short, missing), flagged day, open at end; numbered from <paramref name="firstSeq"/>.</summary>
    public static IReadOnlyList<SpotRow> Choose(IReadOnlyList<SimulationRun> runs, string seed, int firstSeq)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(seed);

        var lines = new List<SpotRow>();
        foreach (var reason in Reasons)
        {
            var code = reason.ToCode();
            var candidates = Candidates(runs, i => i.Notes.Where(n => n.Reason == reason).Select(n => n.Date));
            Add(lines, ExcludedSession, code, candidates, seed, firstSeq);
        }

        Add(lines, FlaggedDay, string.Empty, Candidates(runs, i => i.Discontinuities.Select(d => d.Date)), seed, firstSeq);

        var open = new List<(string Symbol, DateOnly Date, int Run, string Key)>();
        for (var r = 0; r < runs.Count; r++)
        {
            open.AddRange(runs[r].OpenAtEnd.Select(t => (t.Trade.Symbol, t.Trade.TradeDate, r, t.Trade.Key)));
        }

        if (open.Count > 0)
        {
            var best = open.OrderBy(o => SeededRank.Of(seed, "spot|" + OpenAtEnd + "|" + o.Key), StringComparer.Ordinal).ThenBy(o => o.Key, StringComparer.Ordinal).First();
            lines.Add(new SpotRow(firstSeq + lines.Count, OpenAtEnd, string.Empty, best.Symbol, best.Date, best.Run));
        }

        return lines;
    }

    private static List<(string Symbol, DateOnly Date, int Run)> Candidates(IReadOnlyList<SimulationRun> runs, Func<InstrumentRun, IEnumerable<DateOnly>> dates)
    {
        var seen = new HashSet<(string, DateOnly)>();
        var list = new List<(string, DateOnly, int)>();
        for (var r = 0; r < runs.Count; r++)
        {
            foreach (var instrument in runs[r].Instruments)
            {
                foreach (var date in dates(instrument))
                {
                    if (seen.Add((instrument.Symbol, date)))
                    {
                        list.Add((instrument.Symbol, date, r));
                    }
                }
            }
        }

        return list;
    }

    private static void Add(List<SpotRow> lines, string kind, string detail, List<(string Symbol, DateOnly Date, int Run)> candidates, string seed, int firstSeq)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var label = "spot|" + kind + "|" + detail + "|";
        var best = candidates
            .OrderBy(c => SeededRank.Of(seed, label + c.Symbol + "|" + c.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), StringComparer.Ordinal)
            .ThenBy(c => c.Symbol, StringComparer.Ordinal)
            .First();
        lines.Add(new SpotRow(firstSeq + lines.Count, kind, detail, best.Symbol, best.Date, best.Run));
    }
}
