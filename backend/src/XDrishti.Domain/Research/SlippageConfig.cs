using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>Slippage of market-type fills (doc 16 <c>costs.slippage</c>). Only <see cref="Create"/> builds one.</summary>
public sealed record SlippageConfig
{
    public const double MaxTicks = 100;

    private SlippageConfig(double ticks, double barRangeFrac, double stopExitExtraTicks, GapFillRule gapFill)
    {
        Ticks = ticks;
        BarRangeFrac = barRangeFrac;
        StopExitExtraTicks = stopExitExtraTicks;
        GapFill = gapFill;
    }

    public double Ticks { get; }

    public double BarRangeFrac { get; }

    public double StopExitExtraTicks { get; }

    public GapFillRule GapFill { get; }

    /// <summary>The doc 16 defaults: 1 tick, 5 % of the bar range, 2 more ticks on stop exits, gaps fill at the open.</summary>
    public static SlippageConfig Default { get; } = new(1, 0.05, 2, GapFillRule.AtOpen);

    /// <summary>No slippage: the exact layer.</summary>
    public static SlippageConfig None { get; } = new(0, 0, 0, GapFillRule.AtOpen);

    public bool IsNone => Ticks == 0 && BarRangeFrac == 0 && StopExitExtraTicks == 0;

    public static Result<SlippageConfig> Create(double ticks, double barRangeFrac, double stopExitExtraTicks, GapFillRule gapFill = GapFillRule.AtOpen)
    {
        if (!(ticks >= 0 && ticks <= MaxTicks))
        {
            return Result.Failure<SlippageConfig>(SimulationErrors.SlippageInvalid(nameof(Ticks), $"must be a number from 0 to {MaxTicks.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (!(barRangeFrac >= 0 && barRangeFrac <= 1))
        {
            return Result.Failure<SlippageConfig>(SimulationErrors.SlippageInvalid(nameof(BarRangeFrac), "must be a number from 0 to 1"));
        }

        if (!(stopExitExtraTicks >= 0 && stopExitExtraTicks <= MaxTicks))
        {
            return Result.Failure<SlippageConfig>(SimulationErrors.SlippageInvalid(nameof(StopExitExtraTicks), $"must be a number from 0 to {MaxTicks.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (gapFill != GapFillRule.AtOpen)
        {
            return Result.Failure<SlippageConfig>(SimulationErrors.SlippageInvalid(nameof(GapFill), "only 'at open' is supported"));
        }

        // -0.0 passes every check above and is a different double: it would print as "-0" in the canonical JSON. Zero is always +0.
        return Result.Success(new SlippageConfig(PlusZero(ticks), PlusZero(barRangeFrac), PlusZero(stopExitExtraTicks), gapFill));
    }

    private static double PlusZero(double value) => value == 0 ? 0 : value;
}
