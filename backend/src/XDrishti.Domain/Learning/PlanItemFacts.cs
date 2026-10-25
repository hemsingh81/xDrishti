namespace XDrishti.Domain.Learning;

/// <summary>What a stored plan item records about the smart rules (P8-13): its group and the two code lists the plan builder wrote. Nothing else of the item is read.</summary>
/// <param name="Group">primary, reserve or rejected.</param>
/// <param name="Reasons">Reason codes (for a reserve item, why it was moved).</param>
/// <param name="Unavailable">Codes of inputs that were missing or not applied, and the smart-rule flag codes of the plan.</param>
public sealed record PlanItemFacts(string Group, IReadOnlyList<string> Reasons, IReadOnlyList<string> Unavailable);
