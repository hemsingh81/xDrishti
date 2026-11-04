namespace XDrishti.Domain.Lab;

/// <summary>One row of the cost stress (CF-05): a multiplier, the trades, the mean stressed net R, the threshold and comparison it is read against and the reading.</summary>
/// <param name="Multiplier">The factor on charges and slippage.</param>
/// <param name="Trades">The out-of-sample trades restated.</param>
/// <param name="ExpectancyR">The mean stressed net R; null when there is no finite figure.</param>
/// <param name="Threshold">The floor.</param>
/// <param name="Comparison"><c>&gt;=</c> or <c>&gt;</c>.</param>
/// <param name="Status">The reading.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record StressPoint(double Multiplier, int Trades, double? ExpectancyR, double Threshold, string Comparison, StressStatus Status, string Reason);
