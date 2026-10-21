namespace XDrishti.Simulation.Walking;

/// <summary>
/// What a trailing rule needs from the minutes the walker has read (P4-03, D2): <paramref name="Extreme"/> is the highest high (a sell: the lowest low) of the minutes that hold a trade from the fill minute up to, but not including,
/// the minute that sees the bar, which is the anchor <c>H</c> of the chandelier; <paramref name="FirstBar"/> is the first counted bar of the tape (the first that closes after the fill minute started), where a rule looks for its activation.
/// </summary>
internal readonly record struct TrailInput(decimal Extreme, int FirstBar);
