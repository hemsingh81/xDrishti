namespace XDrishti.Simulation.Walking;

/// <summary>The open, high and low of a visited minute as decimals (the close is read only where a rule exits at it).</summary>
internal readonly record struct Minute(decimal Open, decimal High, decimal Low);
