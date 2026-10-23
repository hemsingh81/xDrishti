namespace XDrishti.Domain.Selection;

/// <summary>
/// Ranks merged candidates (doc 10 section 4, spec P7-01): <c>score = expLower + wConf * confluence + wRegime * regimeFit - wCorr * correlation - wEvent * eventRisk</c>. An input that is missing counts as 0 and is listed on the result.
/// The advisory model value is never read. Ties (scores equal to 9 places) fall to the <see cref="StrengthOrder"/>.
/// </summary>
public static class CandidateRanker
{
    /// <summary>Ranks the candidates, best first.</summary>
    public static IReadOnlyList<RankedCandidate> Rank(IEnumerable<MergedCandidate> merged, SelectionRules rules)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(rules);
        var scored = merged.Select(m => (Merged: m, Score: Score(m, rules, out var missing), Missing: missing)).ToList();
        var ordered = scored
            .OrderByDescending(s => Math.Round(s.Score, 9))
            .ThenBy(s => s.Merged.Winner, StrengthOrder.Instance)
            .ToList();
        return [.. ordered.Select((s, i) => new RankedCandidate(i + 1, s.Merged, s.Score, s.Missing))];
    }

    private static double Score(MergedCandidate m, SelectionRules rules, out IReadOnlyList<string> missing)
    {
        var c = m.Winner.Candidate;
        var gaps = new List<string>();
        var regime = Input(c.RegimeFit, "regime_fit", gaps);
        var corr = Input(c.CorrelationWithBook, "correlation_with_book", gaps);
        // Event risk comes from the same data as the event filter: when that filter is unavailable the figure is not trusted, counts 0 and is listed.
        var evt = m.Winner.Results.Any(r => r.Code == FilterCodes.Event && r.Outcome == FilterOutcome.Unavailable) ? Input(null, "event_risk", gaps) : Input(c.EventRisk, "event_risk", gaps);
        missing = gaps.AsReadOnly();
        return m.Winner.ConservativeExpectancyR!.Value
            + (rules.ConfluenceWeight * m.Confluence)
            + (rules.RegimeWeight * regime)
            - (rules.CorrelationWeight * corr)
            - (rules.EventWeight * evt);
    }

    private static double Input(double? value, string name, List<string> gaps)
    {
        if (value is { } v && double.IsFinite(v))
        {
            return v;
        }

        gaps.Add(name);
        return 0;
    }
}
