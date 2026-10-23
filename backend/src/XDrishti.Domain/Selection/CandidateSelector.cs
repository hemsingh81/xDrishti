namespace XDrishti.Domain.Selection;

/// <summary>Filters, merges and ranks (spec P7-01). Pure: no clock, no I/O; the decision date is on each candidate.</summary>
public static class CandidateSelector
{
    /// <summary>Runs the three steps.</summary>
    public static SelectionResult Select(IEnumerable<PlanCandidate> candidates, SelectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rules);
        var evaluated = candidates.Select(c => EligibilityFilters.Evaluate(c, rules)).ToList();
        var merge = CandidateMerger.Merge(evaluated, rules);
        return new SelectionResult(
            CandidateRanker.Rank(merge.Kept, rules),
            merge.Dropped,
            [.. evaluated
                .Where(e => e.Verdict != EligibilityVerdict.Eligible)
                .OrderBy(e => e.Candidate.Symbol, StringComparer.Ordinal)
                .ThenBy(e => e.Candidate.Side, StringComparer.Ordinal)
                .ThenBy(e => e.Candidate.Cell.Key, StringComparer.Ordinal)]);
    }
}
