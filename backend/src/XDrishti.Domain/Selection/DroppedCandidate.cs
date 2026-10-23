namespace XDrishti.Domain.Selection;

/// <summary>A candidate that did not survive the merge, with the reason.</summary>
/// <param name="Candidate">The candidate (eligible before the merge).</param>
/// <param name="Code"><c>merged_duplicate</c>, <c>conflict_lost</c>, <c>conflict_close</c> or <c>conflict_dropped</c>.</param>
/// <param name="Detail">One line in words.</param>
public sealed record DroppedCandidate(PlanCandidate Candidate, string Code, string Detail);
