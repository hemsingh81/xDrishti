namespace XDrishti.Domain.Pairs;

/// <summary>One step of the state machine: the action, the position after it, and a reason when it held because the z-score was unavailable (<c>z_unavailable</c>, to be counted) or the settings were unusable (<c>bad_input</c>).</summary>
public sealed record SignalStep(SignalAction Action, PairPosition Position, PairsReason? Reason);
