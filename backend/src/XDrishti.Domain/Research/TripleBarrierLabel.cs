namespace XDrishti.Domain.Research;

/// <summary>
/// The triple-barrier label of one simulated trade (P6-03, ADR 0065): 1 when the target was reached before the stop and before the time limit, else 0, with how it ended and the result at that moment. Only what is known when the trade has ended; nothing
/// here is a feature of the decision.
/// </summary>
/// <param name="Key">The <see cref="SimTrade.Key"/> of the trade.</param>
/// <param name="Label">1 for <see cref="BarrierOutcome.Target"/>, 0 for every other outcome.</param>
/// <param name="Outcome">The barrier that ended the trade.</param>
/// <param name="Basis">Which result <paramref name="R"/> is.</param>
/// <param name="R">The result at resolution in units of the initial risk, on <paramref name="Basis"/>; a second target for later models.</param>
/// <param name="MinutesToResolution">Wall-clock minutes from the fill minute to the exit minute; 0 when both are the same minute. Overnight minutes of a swing trade are included; use <paramref name="SessionsHeld"/> for the count of sessions (P6-04 should prefer it for swing).</param>
/// <param name="SessionsHeld">The sessions held, as in <see cref="SimTrade.SessionsHeld"/>.</param>
/// <param name="DecisionEpochSeconds">The instant of the decision: the only instant a feature may look back from.</param>
/// <param name="ResolvedEpochSeconds">Start of the exit minute: the end of the label's horizon, which a purged walk-forward must keep clear of its test window. The exit minute is still inside the horizon, so a purge must add one bar (or use start + 60).</param>
/// <param name="HasLegs">The trade left in several fills; the label follows the last exit and the initial target (ADR 0065, D4).</param>
public sealed record TripleBarrierLabel(
    string Key,
    int Label,
    BarrierOutcome Outcome,
    LabelBasis Basis,
    double R,
    long MinutesToResolution,
    int SessionsHeld,
    long DecisionEpochSeconds,
    long ResolvedEpochSeconds,
    bool HasLegs);
