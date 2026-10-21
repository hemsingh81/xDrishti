using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The test-only exit rule of P4-01 (D14), the only <see cref="IExitRule"/> in the repository: at each closed bar it moves the stop to the bar's low (a buy; the high for a sell) when that is tighter, so that the tape (D2) and the move
/// of a level (D6) are exercised, and it asks to take <c>share</c> of the position out at the first bar that closes beyond the entry by one initial risk, so that the legs (D7 to D12) are exercised. A NaN is "no new level". With
/// <c>askLoose</c> it also asks, at every bar with a level, for a stop one risk looser than that level, which the walker must ignore and count (D6). It is not reachable from production code, the model, the verbs or any strategy:
/// <c>NoProductionExitRuleTests</c> holds it to that.
/// </summary>
internal sealed class TestLadderRule(decimal share, bool askLoose = false) : IExitRule
{
    public const string Code = "LADDER";

    public const string LowFeature = "low";

    public const string HighFeature = "high";

    public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
    {
        var low = tape.Value(tape.IndexOf(LowFeature), bar);
        var high = tape.Value(tape.IndexOf(HighFeature), bar);
        if (!double.IsNaN(low) && !double.IsNaN(high) && TickGrid.TryConvert(view.IsBuy ? low : high, out var extreme))
        {
            var stop = view.Stop;
            if (view.IsBuy ? extreme > stop : extreme < stop)
            {
                actions.Add(new ExitAction(ExitActionKind.MoveStop, extreme));
            }

            if (askLoose)
            {
                actions.Add(new ExitAction(ExitActionKind.MoveStop, extreme - ((view.IsBuy ? 1m : -1m) * view.Risk)));
            }
        }

        var close = tape.Close(bar);
        if (view.ExitLegs == 0 && !double.IsNaN(close) && TickGrid.TryConvert(close, out var closed) && (view.IsBuy ? 1m : -1m) * (closed - view.Entry) > view.Risk)
        {
            actions.Add(new ExitAction(ExitActionKind.ExitShare, share, Code));
        }
    }
}
