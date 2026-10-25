using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>One feature a condition read at the decision bar (P3-04d).</summary>
/// <param name="Name">The canonical call, <c>or_high(15)</c>, <c>close</c>.</param>
/// <param name="Timeframe">The timeframe of the feature.</param>
/// <param name="Lag">Closed bars back from the latest closed bar of its timeframe.</param>
/// <param name="Value">The value; null when the feature was not ready.</param>
public sealed record FeatureReadTrace(string Name, Timeframe Timeframe, int Lag, double? Value);
