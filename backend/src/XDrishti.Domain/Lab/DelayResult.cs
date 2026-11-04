namespace XDrishti.Domain.Lab;

/// <summary>The fill-one-minute-later test of CF-05, per signal: how many signals the base filled and the delayed run still filled, the mean R per signal of each (a signal that no longer fills counts as 0 R) and the share kept.</summary>
/// <param name="Signals">Signals the base run filled (the same signals are compared).</param>
/// <param name="DelayedFilled">Of them, the signals the delayed run still filled.</param>
/// <param name="BaseMeanR">Base total R over the signals; null when there is no finite figure.</param>
/// <param name="DelayedMeanR">Delayed total R over the same signals (a missing fill is 0 R).</param>
/// <param name="Retention">Delayed mean per signal over the base mean per signal; null when the base is not above 0.</param>
/// <param name="Required">The least share kept.</param>
/// <param name="Status">The reading.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record DelayResult(int Signals, int DelayedFilled, double? BaseMeanR, double? DelayedMeanR, double? Retention, double Required, StressStatus Status, string Reason);
