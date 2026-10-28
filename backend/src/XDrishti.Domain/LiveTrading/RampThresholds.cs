namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The owner-fixed numbers of the evidence gates and breach rules (spec P11-04, <c>Live:Ramp:*</c>). The defaults are the spec's; the options class validates a configured value at start. Gates at these sizes are a safety brake on gross problems,
/// not proof of an edge.
/// </summary>
public sealed record RampThresholds
{
    public static RampThresholds Defaults { get; } = new();

    public int MinSessions { get; init; } = 20;

    public int MinRoundTripsS1 { get; init; } = 20;

    public int MinRoundTripsS2 { get; init; } = 40;

    /// <summary>Round trips whose R cannot be fixed (no reference risk, a partial exit) as a share of all closed ones.</summary>
    public double MaxUncertainShare { get; init; } = 0.10;

    public int MinSlippageN { get; init; } = 20;

    /// <summary>Mean total slippage in R at or below this passes (the "gap" to the plan).</summary>
    public double MaxSlippageGapR { get; init; } = 0.05;

    /// <summary>The one-sided 95 % upper bound of the mean must also be at or below this.</summary>
    public double MaxSlippageUpperR { get; init; } = 0.10;

    public double MaxDiscretionaryShare { get; init; } = 0.10;

    public int MinDisciplineN { get; init; } = 20;

    public double MinProtectedShare { get; init; } = 0.90;

    public double MaxChasedShare { get; init; } = 0.20;

    public double MinCaptureShare { get; init; } = 0.95;

    public double MaxDrawdownR { get; init; } = 5;

    public int MaxDailyHalts { get; init; } = 1;

    public int HoldCooldownSessions { get; init; } = 5;

    /// <summary>B4 needs at least this many measured entries before the lower bound is looked at.</summary>
    public int BreachSlippageMinN { get; init; } = 15;

    public double CostDragWarnShare { get; init; } = 0.30;

    public bool IsValid =>
        MinSessions > 0 && MinRoundTripsS1 > 0 && MinRoundTripsS2 >= MinRoundTripsS1 && MinSlippageN >= 6 && MinDisciplineN > 0 && MaxDailyHalts >= 0 && HoldCooldownSessions > 0 && BreachSlippageMinN >= 6
        && Share(MaxUncertainShare) && Share(MaxDiscretionaryShare) && Share(MinProtectedShare) && Share(MaxChasedShare) && Share(MinCaptureShare) && Share(CostDragWarnShare)
        && double.IsFinite(MaxSlippageGapR) && MaxSlippageGapR >= 0 && double.IsFinite(MaxSlippageUpperR) && MaxSlippageUpperR >= MaxSlippageGapR && double.IsFinite(MaxDrawdownR) && MaxDrawdownR > 0;

    private static bool Share(double v) => double.IsFinite(v) && v is >= 0 and <= 1;
}
