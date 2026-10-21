namespace XDrishti.Simulation.Walking;

/// <summary>
/// <c>ATR_TRAIL</c> (P4-03, D1 to D3): the chandelier. At every counted bar <i>k</i> with a number for the ATR, once the trail is active, the level is <c>H − mult × ATR</c> (a sell: <c>H + mult × ATR</c>), with <c>H</c> the highest high (a sell:
/// the lowest low) of the minutes from the fill minute through the end of bar <i>k</i>: the anchor is the entry, not the activation. The trail is active from the first counted bar whose close has gone <c>activate_at_rr</c> initial risks
/// in favour (the close, not a touch), and for ever after; with 0 it is active from the first counted bar. The walker takes the level only if it is tighter than the stop in force and counts the others.
/// </summary>
/// <param name="feature">The place of <c>atr(period)</c> in a row of the tape.</param>
/// <param name="multiple">The ATR multiple of the distance.</param>
/// <param name="activateAtRr">The progress, in initial risks, at a bar's close that switches the trail on.</param>
internal sealed class AtrTrailRule(int feature, decimal multiple, decimal activateAtRr) : IExitRule
{
    public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
    {
        if (!Active(view, tape, bar) || !TickGrid.TryConvert(tape.Value(feature, bar), out var atr))
        {
            return;
        }

        var distance = multiple * atr;
        actions.Add(new ExitAction(ExitActionKind.MoveStop, view.IsBuy ? view.Trail.Extreme - distance : view.Trail.Extreme + distance));
    }

    /// <summary>True from the first counted bar up to <paramref name="bar"/> whose close is at least <c>activate_at_rr</c> initial risks in favour of the entry (always, for 0).</summary>
    private bool Active(in ExitView view, ExitTape tape, int bar)
    {
        if (activateAtRr == 0m)
        {
            return true;
        }

        var sign = view.IsBuy ? 1m : -1m;
        for (var j = view.Trail.FirstBar; j <= bar; j++)
        {
            if (TickGrid.TryConvert(tape.Close(j), out var close) && sign * (close - view.Entry) >= activateAtRr * view.Risk)
            {
                return true;
            }
        }

        return false;
    }
}
