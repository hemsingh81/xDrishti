namespace XDrishti.Domain.Selection;

/// <summary>The result of all filters on one candidate.</summary>
/// <param name="Candidate">The candidate.</param>
/// <param name="Verdict">Eligible, rejected or held.</param>
/// <param name="Results">One result per filter, in the order of <see cref="FilterCodes.All"/>.</param>
/// <param name="ConservativeExpectancyR">The expectancy the filter and the rank used; null when it could not be computed.</param>
public sealed record Eligibility(PlanCandidate Candidate, EligibilityVerdict Verdict, IReadOnlyList<FilterResult> Results, double? ConservativeExpectancyR)
{
    /// <summary>True while the cell is below Active: the plan says "provisional: cell not yet paper-proven" (P7-00 Q7).</summary>
    public bool Provisional => Candidate.State != Learning.CellState.Active;

    /// <summary>The results that are not a pass, in filter order: the reasons.</summary>
    public IReadOnlyList<FilterResult> Reasons => [.. Results.Where(r => r.Outcome != FilterOutcome.Pass)];

    /// <summary>Codes of filters that were unavailable on an eligible candidate, which can only be filters the owner loosened through <c>OptionalFilters</c>: shown on the ticket and the list, never a pass.</summary>
    public IReadOnlyList<string> Caveats => Verdict == EligibilityVerdict.Eligible
        ? [.. Results.Where(r => r.Outcome == FilterOutcome.Unavailable).Select(r => r.Code)]
        : [];
}
