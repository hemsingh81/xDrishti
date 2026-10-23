namespace XDrishti.Domain.Selection;

/// <summary>What the merge kept and dropped.</summary>
/// <param name="Kept">One per surviving symbol and side, ordered by symbol then side.</param>
/// <param name="Dropped">Everything else, ordered by symbol, side, cell key.</param>
public sealed record MergeResult(IReadOnlyList<MergedCandidate> Kept, IReadOnlyList<DroppedCandidate> Dropped);
