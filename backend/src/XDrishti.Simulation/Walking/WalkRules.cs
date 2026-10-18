using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>The rules the walker applies to a candidate (ADR 0037, decision D4; ADR 0038): the target, the square-off, how fills are made and the price grid of the instrument.</summary>
/// <param name="RewardRisk">The fixed reward-to-risk multiple of the target (<c>FIXED_RR</c>); null for no target. Must be above 0.</param>
/// <param name="SquareOffMinute">Minute of the India day at which the position leaves at the open of the first minute that holds a trade: 915 for 15:15.</param>
/// <param name="Fills">The entry limit percent and the slippage; built with <see cref="FillRules.Create"/>. A value that bypassed it (null) is refused by the walker.</param>
/// <param name="Tick">The instrument's tick size in rupees; built with <see cref="TickSpec.FromMasterPaise"/> or <see cref="TickSpec.FromRupees"/>. A null value is refused by the walker.</param>
public readonly record struct WalkRules(double? RewardRisk, int SquareOffMinute, FillRules Fills, TickSpec Tick);
