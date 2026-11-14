namespace XDrishti.SimReader;

/// <summary>The recorded definition of VWAP_PULLBACK the reader needs (spec P3-05 D1 to D6): 15-minute bars, decision window 10:00 to 14:30, square-off 15:15, limit 0.1 %.</summary>
/// <param name="PullAtr">How close the dip bar's extreme comes to VWAP, in ATRs.</param>
/// <param name="RewardRisk">Target distance in multiples of the risk.</param>
/// <param name="Tick">The instrument's tick in rupees.</param>
public sealed record VwapPullbackStrategy(decimal PullAtr, decimal RewardRisk, decimal Tick)
{
    public TimeOnly FirstEntry { get; init; } = new(10, 0);

    public TimeOnly LastEntry { get; init; } = new(14, 30);

    public TimeOnly SquareOff { get; init; } = new(15, 15);

    public decimal EntryLimitPercent { get; init; } = 0.1m;

    public static VwapPullbackStrategy V1(decimal tick) => new(0.5m, 2m, tick);
}
