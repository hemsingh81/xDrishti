namespace XDrishti.Simulation;

/// <summary>The identity and the bounds of the simulation rules (ADR 0037).</summary>
public static class SimulationEngine
{
    /// <summary>
    /// The version of the simulation rules, stored in the run record of P3-04. Bumped by any change of decisions D3 to D5 of ADR 0037 or of the rule table of ADR 0038, of the <c>SimTrade</c> schema or of a replay rule of P3-04; a changed rule also regenerates the golden
    /// file of the tests, on purpose.
    /// </summary>
    public const string Version = "sim-6";

    /// <summary>
    /// How many minutes after the decision instant the fill minute may start. A market-type fill at the open of a minute far from the signal is a price unrelated to it; later is no trade
    /// (<c>NoTradeReason.NoFillBar</c>). An engineering bound, not a trading choice (ADR 0037, decision D3).
    /// </summary>
    public const int MaxFillDelayMinutes = 5;
}
