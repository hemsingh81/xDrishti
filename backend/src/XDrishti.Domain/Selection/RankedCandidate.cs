namespace XDrishti.Domain.Selection;

/// <summary>A kept candidate with its place.</summary>
/// <param name="Rank">1 for the best.</param>
/// <param name="Merged">The merged candidate.</param>
/// <param name="Score">The rank score of doc 10 section 4.</param>
/// <param name="MissingInputs">Score inputs that were not available and counted as 0: shown, not hidden.</param>
public sealed record RankedCandidate(int Rank, MergedCandidate Merged, double Score, IReadOnlyList<string> MissingInputs)
{
    /// <summary>True while the cell is below Active.</summary>
    public bool Provisional => Merged.Winner.Provisional;

    /// <summary>The model's P(win), shown beside the cell figures and never used in the score or the filters.</summary>
    public double? AdvisoryPWin => Merged.Winner.Candidate.AdvisoryPWin;
}
