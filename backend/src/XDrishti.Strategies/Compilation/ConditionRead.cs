using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Compilation;

/// <summary>One feature read of a condition (P3-04d): which of <see cref="CompiledCondition.Features"/> it is, how it is written, and how many closed bars back it is read.</summary>
/// <param name="FeatureIndex">Position in <see cref="CompiledCondition.Features"/>.</param>
/// <param name="Name">The feature as written in a call, <c>or_high(15)</c>, <c>close</c>.</param>
/// <param name="Timeframe">The timeframe of the feature.</param>
/// <param name="Lag">Closed bars back from the last closed bar of its timeframe (0 is the latest).</param>
public sealed record ConditionRead(int FeatureIndex, string Name, Timeframe Timeframe, int Lag);
