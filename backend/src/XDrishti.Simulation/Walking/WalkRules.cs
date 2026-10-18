namespace XDrishti.Simulation.Walking;

/// <summary>The exit rules the walker applies to a candidate (ADR 0037, decision D4). P3-03b adds the fill rules and the tick.</summary>
/// <param name="RewardRisk">The fixed reward-to-risk multiple of the target (<c>FIXED_RR</c>); null for no target. Must be above 0.</param>
/// <param name="SquareOffMinute">Minute of the India day at which the position leaves at the open: 915 for 15:15.</param>
public readonly record struct WalkRules(double? RewardRisk, int SquareOffMinute);
