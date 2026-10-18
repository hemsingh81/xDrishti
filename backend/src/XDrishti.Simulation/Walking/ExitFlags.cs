namespace XDrishti.Simulation.Walking;

/// <summary>The facts about an exit that the counters sum.</summary>
/// <param name="StopGap">The exit minute opened strictly beyond the stop.</param>
/// <param name="TargetGap">The exit minute opened strictly beyond the target.</param>
/// <param name="Ambiguous">The exit minute reached both levels with its open strictly between them.</param>
/// <param name="LateSquareOff">The square-off happened at a minute after the square-off minute.</param>
/// <param name="MaxHoldAtLastClose">The due minute held no trade and the position left at the close of the last minute that did.</param>
/// <param name="Overnight">The exit minute is the first that holds a trade on a date later than the entry date.</param>
internal readonly record struct ExitFlags(bool StopGap = false, bool TargetGap = false, bool Ambiguous = false, bool LateSquareOff = false, bool MaxHoldAtLastClose = false, bool Overnight = false);
