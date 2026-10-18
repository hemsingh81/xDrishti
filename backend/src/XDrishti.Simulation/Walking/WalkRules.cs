using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>The rules the walker applies to a candidate (ADR 0037, decision D4; ADR 0038; ADR 0040): the target, the square-off, how fills are made, the price grid of the instrument and, for a swing walk, how long the position may be held.</summary>
/// <param name="RewardRisk">The fixed reward-to-risk multiple of the target (<c>FIXED_RR</c>); null for no target. Must be above 0.</param>
/// <param name="SquareOffMinute">Minute of the India day at which the position leaves at the open of the first minute that holds a trade: 915 for 15:15. <see cref="WalkRules.NoSquareOff"/> (0) for a swing walk, which has none.</param>
/// <param name="Fills">The entry limit percent and the slippage; built with <see cref="FillRules.Create"/>. A value that bypassed it (null) is refused by the walker.</param>
/// <param name="Tick">The instrument's tick size in rupees; built with <see cref="TickSpec.FromMasterPaise"/> or <see cref="TickSpec.FromRupees"/>. A null value is refused by the walker.</param>
/// <param name="Hold">The longest hold in sessions and the calendar that counts them; null for the intraday walk of ADR 0038, which ends at the square-off. A walk with a hold is a swing walk (X7 to X9) and its square-off must be <see cref="WalkRules.NoSquareOff"/>.</param>
public readonly record struct WalkRules(double? RewardRisk, int SquareOffMinute, FillRules Fills, TickSpec Tick, SwingHold? Hold = null)
{
    /// <summary>The square-off minute of a swing walk: there is none (X7).</summary>
    public const int NoSquareOff = 0;
}
