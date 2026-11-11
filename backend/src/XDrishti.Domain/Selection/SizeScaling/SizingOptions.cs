using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// The numbers of the down-only size scaling (spec BK-10). The ceiling of the volatility scale is not here: it is the constant <see cref="VolTarget.Ceiling"/>, so no value of this record can scale a quantity up.
/// </summary>
/// <param name="Lambda">EWMA decay, 0.94 (RiskMetrics, daily data).</param>
/// <param name="Floor">The lowest volatility scale, 0.5. 0.25 to 1.0.</param>
/// <param name="MinReturns">Returns needed before any volatility figure exists, 60.</param>
/// <param name="MinHistory">Volatility values needed before a target exists, 120.</param>
/// <param name="CapacityPct">Share of the 20-session median traded value one suggested quantity may be: at most (and by default) the configured <c>Confidence:CostStress:CapacityDailyShare</c>, 0.005. It can only be tightened below it.</param>
/// <param name="MinFit">The lowest share of its quantity a candidate may be cut to by its basket budget (below it the candidate goes to the reserve), 0.5. 0.25 to 1.0.</param>
public sealed record SizingOptions(double Lambda = 0.94, double Floor = 0.5, int MinReturns = 60, int MinHistory = 120, double CapacityPct = (double)CostStressOptions.DefaultCapacityDailyShare, double MinFit = 0.5)
{
    /// <summary>The returns that seed the first variance.</summary>
    public const int SeedReturns = 20;

    /// <summary>The sessions whose median volatility is the target.</summary>
    public const int TargetSessions = 252;

    /// <summary>The lowest allowed <see cref="Floor"/>.</summary>
    public const double LowestFloor = 0.25;

    /// <summary>The highest allowed <see cref="Floor"/>.</summary>
    public const double HighestFloor = 1.0;

    /// <summary>The highest allowed <see cref="CapacityPct"/>: the default of the one capacity share of the app (ADR 0223), not a second constant. The configured value may be lower; it is the ceiling then.</summary>
    public const double HighestCapacityPct = (double)CostStressOptions.DefaultCapacityDailyShare;

    /// <summary>True when <paramref name="floor"/> is a number from 0.25 to 1.0.</summary>
    public static bool FloorIsValid(double floor) => double.IsFinite(floor) && floor is >= LowestFloor and <= HighestFloor;

    /// <summary>True when <paramref name="pct"/> is above 0 and at most <paramref name="ceiling"/> (default 0.005).</summary>
    public static bool CapacityPctIsValid(double pct, double ceiling = HighestCapacityPct) => double.IsFinite(pct) && pct > 0 && pct <= Math.Min(ceiling, HighestCapacityPct);
}
