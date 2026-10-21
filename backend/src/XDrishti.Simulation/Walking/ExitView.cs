namespace XDrishti.Simulation.Walking;

/// <summary>What an exit rule may see of the position (P4-01, D4): its side, the exact entry, the initial risk <c>R0</c> (the unit of R, never changed), the stop and the target now, the share still open and the exit legs taken so far.</summary>
internal readonly record struct ExitView(bool IsBuy, decimal Entry, decimal Risk, decimal Stop, decimal? Target, decimal OpenShare, int ExitLegs);
