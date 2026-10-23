namespace XDrishti.Domain.Learning;

/// <summary>A reason to think about a hypothesis (spec P6-10): a proposal to reflect, never a hypothesis and never an action.</summary>
/// <param name="Kind"><c>new_trades</c> or <c>drift</c>.</param>
/// <param name="CellKey">The cell.</param>
/// <param name="Detail">The numbers behind it.</param>
public sealed record HypothesisTrigger(string Kind, string CellKey, string Detail);
