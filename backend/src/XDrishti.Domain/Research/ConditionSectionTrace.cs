namespace XDrishti.Domain.Research;

/// <summary>One list of a side at the decision bar (P3-04d): <c>setup</c>, <c>trigger</c> or <c>invalidate</c>.</summary>
/// <param name="SectionName"><c>setup</c>, <c>trigger</c> or <c>invalidate</c>.</param>
/// <param name="Mode"><c>all</c> or <c>any</c>.</param>
/// <param name="Items">The conditions, in the order of the strategy.</param>
public sealed record ConditionSectionTrace(string SectionName, string Mode, IReadOnlyList<ConditionItemTrace> Items);
