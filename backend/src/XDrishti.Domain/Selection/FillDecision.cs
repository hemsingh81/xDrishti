namespace XDrishti.Domain.Selection;

/// <summary>What the fill did with one candidate.</summary>
/// <param name="Candidate">The candidate.</param>
/// <param name="Status">Primary, reserve or rejected.</param>
/// <param name="Checks">Every check at the candidate's turn, in code order; for a primary all pass.</param>
public sealed record FillDecision(FillCandidate Candidate, FillStatus Status, IReadOnlyList<FillCheck> Checks)
{
    /// <summary>The checks that are not a pass: why the candidate was not taken.</summary>
    public IReadOnlyList<FillCheck> Reasons => [.. Checks.Where(c => c.Outcome != FilterOutcome.Pass)];
}
