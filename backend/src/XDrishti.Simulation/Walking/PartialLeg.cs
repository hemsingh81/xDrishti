namespace XDrishti.Simulation.Walking;

/// <summary>A partial exit of a walk, in decimal: the minute it filled in, the exact and the filled price, the share of the maximum quantity and the code of the rule that asked.</summary>
internal readonly record struct PartialLeg(long Epoch, decimal Price, decimal Filled, decimal Share, string Code);
