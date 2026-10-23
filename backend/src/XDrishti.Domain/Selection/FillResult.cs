namespace XDrishti.Domain.Selection;

/// <summary>The portfolio fill: every input candidate appears exactly once, each list in rank order.</summary>
/// <param name="Primary">The plan.</param>
/// <param name="Reserve">Stand-ins, with the reason each was not taken.</param>
/// <param name="Rejected">Not admissible.</param>
public sealed record FillResult(IReadOnlyList<FillDecision> Primary, IReadOnlyList<FillDecision> Reserve, IReadOnlyList<FillDecision> Rejected);
