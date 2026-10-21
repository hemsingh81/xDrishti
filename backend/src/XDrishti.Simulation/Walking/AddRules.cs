namespace XDrishti.Simulation.Walking;

/// <summary>What a pyramid needs from the fill rules to make an add (P4-04, D3): the entry limit as a fraction, the tick of the instrument and the square-off minute (<see cref="int.MaxValue"/> for a swing walk, which has none).</summary>
internal readonly record struct AddRules(decimal LimitFraction, decimal Tick, int SquareOffMinute);
