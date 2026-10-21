using XDrishti.Simulation.Walking;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Running;

/// <summary>The one place a compiled trail policy becomes the <see cref="IExitRule"/> that reads the tape of its scanner (P4-03).</summary>
internal static class TrailRules
{
    /// <summary>The rule of <paramref name="plan"/> (<c>ATR_TRAIL</c>, <c>STRUCTURE_TRAIL</c>, <c>EMA_TRAIL</c> or the trail of <c>PYRAMID</c>), bound to the column of its feature in the tape (<see cref="Scanning.EntryScanner.TapeColumnOf"/>).</summary>
    public static IExitRule Of(ExitPlan plan, int feature)
    {
        switch (plan.Policy)
        {
            case ExitPolicy.AtrTrail:
                _ = TickGrid.TryConvert(plan.Values["mult"][0], out var multiple);
                _ = TickGrid.TryConvert(plan.Values["activate_at_rr"][0], out var activate);
                return new AtrTrailRule(feature, multiple, activate);
            case ExitPolicy.Pyramid:
                // P4-04, D5: the chandelier of the combined position, active from the first counted bar.
                _ = TickGrid.TryConvert(plan.Values["trail_atr_mult"][0], out var pyramidMultiple);
                return new AtrTrailRule(feature, pyramidMultiple, 0m);
            case ExitPolicy.StructureTrail:
                return new StructureTrailRule(feature);
            case ExitPolicy.EmaTrail:
                return new EmaTrailRule(feature);
            default:
                throw new InvalidOperationException("The simulator runs no trail for " + plan.Policy + ".");
        }
    }
}
