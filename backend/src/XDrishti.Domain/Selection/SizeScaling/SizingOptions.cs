namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// The numbers of the down-only size scaling (spec BK-10). The ceiling of the volatility scale is not here: it is the constant <see cref="VolTarget.Ceiling"/>, so no value of this record can scale a quantity up.
/// </summary>
/// <param name="Lambda">EWMA decay, 0.94 (RiskMetrics, daily data).</param>
/// <param name="Floor">The lowest volatility scale, 0.5; also the lowest share of its quantity a candidate may be cut to by its basket budget. 0.25 to 1.0.</param>
/// <param name="MinReturns">Returns needed before any volatility figure exists, 60.</param>
/// <param name="MinHistory">Volatility values needed before a target exists, 120.</param>
/// <param name="CapacityPct">Share of the 20-session median traded value one suggested quantity may be, 0.01 (1 %).</param>
public sealed record SizingOptions(double Lambda = 0.94, double Floor = 0.5, int MinReturns = 60, int MinHistory = 120, double CapacityPct = 0.01)
{
    /// <summary>The returns that seed the first variance.</summary>
    public const int SeedReturns = 20;

    /// <summary>The sessions whose median volatility is the target.</summary>
    public const int TargetSessions = 252;

    /// <summary>The lowest allowed <see cref="Floor"/>.</summary>
    public const double LowestFloor = 0.25;

    /// <summary>The highest allowed <see cref="Floor"/>.</summary>
    public const double HighestFloor = 1.0;

    /// <summary>The highest allowed <see cref="CapacityPct"/>.</summary>
    public const double HighestCapacityPct = 0.10;

    /// <summary>True when <paramref name="floor"/> is a number from 0.25 to 1.0.</summary>
    public static bool FloorIsValid(double floor) => double.IsFinite(floor) && floor is >= LowestFloor and <= HighestFloor;

    /// <summary>True when <paramref name="pct"/> is above 0 and at most 0.10.</summary>
    public static bool CapacityPctIsValid(double pct) => double.IsFinite(pct) && pct is > 0 and <= HighestCapacityPct;
}
