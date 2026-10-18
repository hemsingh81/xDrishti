namespace XDrishti.Simulation;

/// <summary>One feature a condition reads and its values at the decision step: <paramref name="Feature"/> is written <c>close@15m</c>, <c>or_high(15)@15m</c>; <c>Lags[k]</c> is the value <c>k</c> closed bars of its own timeframe ago (NaN where there is none).</summary>
public sealed record TraceValue(string Feature, IReadOnlyList<double> Lags);
