namespace XDrishti.Simulation.Walking;

/// <summary>
/// <c>EMA_TRAIL</c> (P4-03, D6, D7): at every counted bar whose EMA is a number, a close beyond the EMA (a buy: below; a sell: above) signals the whole position out. The walker leaves at the open of the next minute that holds a trade, as
/// <see cref="XDrishti.Domain.Research.ExitReason.EmaExit"/>, unless that open is already through the initial stop.
/// </summary>
/// <param name="feature">The place of <c>ema(length)</c> in a row of the tape.</param>
internal sealed class EmaTrailRule(int feature) : IExitRule
{
    public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
    {
        if (TickGrid.TryConvert(tape.Value(feature, bar), out var ema) && TickGrid.TryConvert(tape.Close(bar), out var close) && (view.IsBuy ? close < ema : close > ema))
        {
            actions.Add(new ExitAction(ExitActionKind.SignalExit, 0m));
        }
    }
}
