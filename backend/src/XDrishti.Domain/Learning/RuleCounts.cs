namespace XDrishti.Domain.Learning;

/// <summary>What the stored plans say about one smart rule (P8-13). Counts only; no outcome.</summary>
/// <param name="Id">gate, throttle, correlation, meta or windows.</param>
/// <param name="Recorded">The plan rows carry codes for this rule at all; false for the rules that leave no code (meta labelling, entry windows), whose counts are then not known.</param>
/// <param name="UnavailableOn">Plans carrying a code ending in <c>_unavailable</c> for the rule.</param>
/// <param name="PartialOn">Plans carrying <c>correlation_partial</c> (some pairs not checked); 0 for the other rules.</param>
/// <param name="Moved">Candidates the rule moved to reserve, by reserve reason in <paramref name="Reasons"/>.</param>
/// <param name="Reasons">Reserve reasons of the rule with their counts (zeros included, so the table is stable).</param>
public sealed record RuleCounts(string Id, bool Recorded, int UnavailableOn, int PartialOn, int Moved, IReadOnlyList<ReasonCount> Reasons);
