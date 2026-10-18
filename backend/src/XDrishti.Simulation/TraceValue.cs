namespace XDrishti.Simulation;

/// <summary>One feature read by a condition at the moment of a decision, with its recent values.</summary>
/// <param name="Feature">The feature and its timeframe, for example <c>close@15m</c>.</param>
/// <param name="Lags"><c>Lags[k]</c> is the value k closed bars of its own timeframe ago.</param>
public sealed record TraceValue(string Feature, IReadOnlyList<double> Lags);
