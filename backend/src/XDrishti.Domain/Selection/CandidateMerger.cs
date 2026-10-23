using System.Globalization;

namespace XDrishti.Domain.Selection;

/// <summary>
/// Merges duplicates (doc 10 section 3, spec P7-01). Only eligible candidates are read. Group by (symbol, side); the winner has the highest conservative expectancy, then the higher shrunk win rate, the more trades, the smaller cell key (ordinal): a total order, so
/// the input order never matters. Confluence is the number of other distinct strategies in the group (timeframes of one strategy are not independent), capped. For a symbol with both sides, the side with the higher <c>expectancy + confluence weight * confluence</c> survives when it
/// leads by more than the margin, otherwise both are dropped; the policy <see cref="ConflictPolicy.DropBoth"/> always drops both.
/// </summary>
public static class CandidateMerger
{
    /// <summary>Merges the eligible entries of <paramref name="evaluated"/>.</summary>
    public static MergeResult Merge(IEnumerable<Eligibility> evaluated, SelectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(evaluated);
        ArgumentNullException.ThrowIfNull(rules);
        var dropped = new List<DroppedCandidate>();
        var winners = new List<MergedCandidate>();
        var groups = evaluated
            .Where(e => e.Verdict == EligibilityVerdict.Eligible && e.ConservativeExpectancyR is not null)
            .GroupBy(e => (e.Candidate.Symbol, e.Candidate.Side));
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(e => e, StrengthOrder.Instance).ToList();
            var winner = ordered[0];
            var others = ordered.Skip(1).ToList();
            var confluence = Math.Min(rules.MaxConfluence, others.Select(o => o.Candidate.Cell.Strategy).Where(s => s != winner.Candidate.Cell.Strategy).Distinct(StringComparer.Ordinal).Count());
            foreach (var o in others)
            {
                dropped.Add(new DroppedCandidate(o.Candidate, "merged_duplicate", "merged into " + winner.Candidate.Cell.Key));
            }

            winners.Add(new MergedCandidate(winner, confluence, [.. others.Select(o => o.Candidate.Cell.Key)]));
        }

        var kept = new List<MergedCandidate>();
        foreach (var bySymbol in winners.GroupBy(w => w.Winner.Candidate.Symbol, StringComparer.Ordinal))
        {
            var sides = bySymbol.OrderBy(w => w.Winner.Candidate.Side, StringComparer.Ordinal).ToList();
            if (sides.Count < 2)
            {
                kept.AddRange(sides);
                continue;
            }

            Resolve(sides, rules, kept, dropped);
        }

        return new MergeResult(
            [.. kept.OrderBy(k => k.Winner.Candidate.Symbol, StringComparer.Ordinal).ThenBy(k => k.Winner.Candidate.Side, StringComparer.Ordinal)],
            [.. dropped.OrderBy(d => d.Candidate.Symbol, StringComparer.Ordinal).ThenBy(d => d.Candidate.Side, StringComparer.Ordinal).ThenBy(d => d.Candidate.Cell.Key, StringComparer.Ordinal)]);
    }

    private static void Resolve(List<MergedCandidate> sides, SelectionRules rules, List<MergedCandidate> kept, List<DroppedCandidate> dropped)
    {
        var scored = sides.Select(s => (Side: s, Strength: s.Winner.ConservativeExpectancyR!.Value + (rules.ConfluenceWeight * s.Confluence))).OrderByDescending(x => x.Strength).ThenBy(x => x.Side.Winner.Candidate.Side, StringComparer.Ordinal).ToList();
        var lead = scored[0].Strength - scored[1].Strength;
        if (rules.ConflictPolicy == ConflictPolicy.KeepStrongerOrDropBoth && Math.Round(lead, 9) > rules.ConflictMargin)
        {
            kept.Add(scored[0].Side);
            foreach (var loser in scored.Skip(1))
            {
                AddAll(dropped, loser.Side, "conflict_lost", string.Create(CultureInfo.InvariantCulture, $"opposite side {scored[0].Side.Winner.Candidate.Side} is stronger by {lead:0.####} R"));
            }

            return;
        }

        var code = rules.ConflictPolicy == ConflictPolicy.DropBoth ? "conflict_dropped" : "conflict_close";
        foreach (var s in scored)
        {
            AddAll(dropped, s.Side, code, string.Create(CultureInfo.InvariantCulture, $"both sides eligible, lead {lead:0.####} R, margin {rules.ConflictMargin:0.####} R"));
        }
    }

    private static void AddAll(List<DroppedCandidate> dropped, MergedCandidate merged, string code, string detail)
    {
        dropped.Add(new DroppedCandidate(merged.Winner.Candidate, code, detail));
    }
}
