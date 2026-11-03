using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// The candidate pairs of a formation (P14-13): every pair of members of one sector (today's label) whose correlation of daily log returns over the window is at least <c>MinCorrelation</c>; more than <c>MaxCandidates</c> keeps the most correlated, ties by the symbols' ordinal order.
/// <c>y</c> is the member with the larger 20-session mean turnover (a tie: the symbol that sorts first). A pair whose correlation cannot be computed is not a candidate and is counted. Pure.
/// </summary>
public static class PairUniverse
{
    /// <summary>The candidates in the order the cap ranks them (most correlated first).</summary>
    public static PairCandidateSet Candidates(IReadOnlyList<PairMember> members, PairStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        var logs = new double[members.Count][];
        for (var i = 0; i < members.Count; i++)
        {
            var ln = LogSpread.Ln([.. members[i].Closes]);
            logs[i] = ln.TryGet(out var values) ? values : [];
        }

        var found = new List<(int A, int B, double Correlation)>();
        int same = 0, unavailable = 0, below = 0;
        var noSector = 0;
        foreach (var sector in members.Select((m, i) => (m, i)).Where(t => !string.IsNullOrWhiteSpace(t.m.Sector)).GroupBy(t => t.m.Sector, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = sector.OrderBy(t => t.m.Symbol, StringComparer.Ordinal).Select(t => t.i).ToArray();
            for (var a = 0; a < ordered.Length; a++)
            {
                for (var b = a + 1; b < ordered.Length; b++)
                {
                    same++;
                    var ia = ordered[a];
                    var ib = ordered[b];
                    var rho = logs[ia].Length == 0 || logs[ib].Length == 0 ? default : ReturnCorrelation.Of(logs[ia], logs[ib], options.Core);
                    if (!rho.TryGet(out var r))
                    {
                        unavailable++;
                    }
                    else if (r >= options.Core.MinCorrelation)
                    {
                        found.Add((ia, ib, r));
                    }
                    else
                    {
                        below++;
                    }
                }
            }
        }

        foreach (var m in members)
        {
            if (string.IsNullOrWhiteSpace(m.Sector))
            {
                noSector++;
            }
        }

        var ranked = found
            .OrderByDescending(f => Math.Round(f.Correlation, 12)) // numerical noise in the last digits must not decide a tie
            .ThenBy(f => string.CompareOrdinal(members[f.A].Symbol, members[f.B].Symbol) <= 0 ? members[f.A].Symbol : members[f.B].Symbol, StringComparer.Ordinal)
            .ThenBy(f => string.CompareOrdinal(members[f.A].Symbol, members[f.B].Symbol) <= 0 ? members[f.B].Symbol : members[f.A].Symbol, StringComparer.Ordinal)
            .ToList();
        var capped = Math.Max(0, ranked.Count - options.MaxCandidates);
        var candidates = ranked.Take(options.MaxCandidates).Select(f =>
        {
            var a = members[f.A];
            var b = members[f.B];
            var aIsY = a.MeanTurnover > b.MeanTurnover || (a.MeanTurnover == b.MeanTurnover && string.CompareOrdinal(a.Symbol, b.Symbol) <= 0);
            return aIsY ? new PairCandidate(a.Symbol, b.Symbol, f.A, f.B, f.Correlation) : new PairCandidate(b.Symbol, a.Symbol, f.B, f.A, f.Correlation);
        }).ToList();
        return new PairCandidateSet(candidates, same, noSector, unavailable, below, capped);
    }
}
