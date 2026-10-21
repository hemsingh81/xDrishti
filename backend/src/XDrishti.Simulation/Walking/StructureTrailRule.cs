namespace XDrishti.Simulation.Walking;

/// <summary>
/// <c>STRUCTURE_TRAIL</c> (P4-03, D4, D5): at every counted bar the stop goes to the lowest low of the last <c>lookback_bars</c> bars including that one (a sell: the highest high), as the tape holds it. No activation and no offset; a value
/// that is not a number gives no level, and the walker takes a level only if it is tighter than the stop in force.
/// </summary>
/// <param name="feature">The place of <c>lowest(n, low)</c> (a sell: <c>highest(n, high)</c>) in a row of the tape.</param>
internal sealed class StructureTrailRule(int feature) : IExitRule
{
    public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
    {
        if (TickGrid.TryConvert(tape.Value(feature, bar), out var level))
        {
            actions.Add(new ExitAction(ExitActionKind.MoveStop, level));
        }
    }
}
