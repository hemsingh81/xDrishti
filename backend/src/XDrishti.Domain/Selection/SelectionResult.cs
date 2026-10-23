namespace XDrishti.Domain.Selection;

/// <summary>The whole selection core on a set of candidates.</summary>
/// <param name="Ranked">Surviving candidates, best first.</param>
/// <param name="Dropped">Eligible candidates lost in the merge.</param>
/// <param name="NotEligible">Rejected and held candidates with their reasons, ordered by symbol, side, cell key.</param>
public sealed record SelectionResult(IReadOnlyList<RankedCandidate> Ranked, IReadOnlyList<DroppedCandidate> Dropped, IReadOnlyList<Eligibility> NotEligible);
