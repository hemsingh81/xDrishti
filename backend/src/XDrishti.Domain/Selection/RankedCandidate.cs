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

    /// <summary>Filters that were unavailable but loosened by the owner: shown, never a pass.</summary>
    public IReadOnlyList<string> Caveats => Merged.Winner.Caveats;

    /// <summary>The score in words, with the inputs that counted as 0 because they were not available.</summary>
    public string Explanation => MissingInputs.Count == 0
        ? "score " + Score.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " R"
        : "score " + Score.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " R, not available and counted as 0: " + string.Join(", ", MissingInputs);

    /// <summary>The model's P(win), shown beside the cell figures and never used in the score or the filters.</summary>
    public double? AdvisoryPWin => Merged.Winner.Candidate.AdvisoryPWin;
}
