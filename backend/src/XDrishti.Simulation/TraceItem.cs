namespace XDrishti.Simulation;

/// <summary>One condition of a strategy as it stood at the moment of a decision.</summary>
/// <param name="Path">Where the condition is in the strategy, for example <c>buy.trigger[0]</c>.</param>
/// <param name="Source">The text of the condition.</param>
/// <param name="Truth">Its value.</param>
/// <param name="Values">The features it read.</param>
public sealed record TraceItem(string Path, string Source, TraceTruth Truth, IReadOnlyList<TraceValue> Values);
