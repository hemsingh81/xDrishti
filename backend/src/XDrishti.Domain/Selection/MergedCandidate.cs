namespace XDrishti.Domain.Selection;

/// <summary>The one candidate kept for a symbol and side (doc 10 section 3).</summary>
/// <param name="Winner">The strongest eligible candidate of the group.</param>
/// <param name="Confluence">How many other independent strategies agreed (capped by the rules).</param>
/// <param name="MergedCells">Keys of the cells merged into the winner, in the order of the total order.</param>
public sealed record MergedCandidate(Eligibility Winner, int Confluence, IReadOnlyList<string> MergedCells);
