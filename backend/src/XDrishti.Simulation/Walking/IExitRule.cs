namespace XDrishti.Simulation.Walking;

/// <summary>
/// One candidate exit beyond the fixed stop and target (P4-01, D5). The walker calls it once for each closed bar of the <see cref="ExitTape"/> that it sees for the first time, in order, at the first minute that starts at or after the
/// bar's close (D2), and applies the actions it appends in order. A rule is a pure function of the view, the tape up to the bar it is given and nothing else, so it can carry no state of its own. <c>FIXED_RR</c> is the absence of a
/// rule: its stop and target are the position's.
/// </summary>
internal interface IExitRule
{
    /// <summary>Reads bar <paramref name="bar"/> of the tape (never a later one) and appends what it asks for to <paramref name="actions"/>.</summary>
    void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions);
}
