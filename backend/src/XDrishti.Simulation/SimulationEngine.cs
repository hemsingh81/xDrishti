namespace XDrishti.Simulation;

/// <summary>
/// The version of the simulation rules and the limits they share (P3-03, ADR 0037). <see cref="Version"/> is bumped by any change of the entry, exit or R rules, of the schema of a simulated trade or of a replay rule, so that a
/// stored result says which rules made it.
/// </summary>
public static class SimulationEngine
{
    /// <summary>The version of the rules.</summary>
    public const string Version = "sim-1";

    /// <summary>The most minutes the first minute at or after the decision instant may start after it for the order to still fill (the entry is a market-type fill at the open of that minute).</summary>
    public const int MaxFillDelayMinutes = 5;
}
