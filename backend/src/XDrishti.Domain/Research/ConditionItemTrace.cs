namespace XDrishti.Domain.Research;

/// <summary>One condition of a list at the decision bar (P3-04d).</summary>
/// <param name="Path">Where it is in the strategy, <c>buy.trigger[0]</c>.</param>
/// <param name="Text">The text of the condition.</param>
/// <param name="Truth">Its value.</param>
/// <param name="Reads">The features it read, in text order, each with its lag.</param>
/// <param name="ReadsOmitted">True when the condition reads more than the listed reads (the list is capped).</param>
public sealed record ConditionItemTrace(string Path, string Text, TruthState Truth, IReadOnlyList<FeatureReadTrace> Reads, bool ReadsOmitted = false);
