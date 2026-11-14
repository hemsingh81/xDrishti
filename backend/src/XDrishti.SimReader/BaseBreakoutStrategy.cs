namespace XDrishti.SimReader;

/// <summary>The recorded definition of BASE_BREAKOUT v1 the reader needs (spec P3-06 D1, D2, D4): daily bars, buy only, a hold of at most five sessions, limit 0.1 % of the signal close.</summary>
/// <param name="BaseLen">Sessions in the base window, also the lookback of the stop.</param>
/// <param name="BasePct">The widest span of the window's closes, percent of the lowest.</param>
/// <param name="VolX">The least multiple of the mean volume of the 20 sessions before.</param>
/// <param name="RewardRisk">Target distance in multiples of the risk.</param>
/// <param name="Tick">The instrument's tick in rupees.</param>
public sealed record BaseBreakoutStrategy(int BaseLen, decimal BasePct, decimal VolX, decimal RewardRisk, decimal Tick)
{
    public int MaxHoldSessions { get; init; } = 5;

    public decimal EntryLimitPercent { get; init; } = 0.1m;

    public static BaseBreakoutStrategy V1(decimal tick) => new(20, 8m, 1.5m, 2m, tick);
}
