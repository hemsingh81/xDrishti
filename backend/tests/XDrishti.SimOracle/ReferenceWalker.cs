namespace XDrishti.SimOracle;

/// <summary>The reference walk of one candidate over the 1-minute bars (ADR 0038, 0040, 0043 and the rules of ADR 0047).</summary>
public static class ReferenceWalker
{
    /// <summary>Walks one candidate. All arithmetic is in decimal; doubles are converted by rule R1.</summary>
    public static OracleResult Walk(IReadOnlyList<OracleBar> bars, OracleCandidate candidate, OracleRules rules, OracleFaults faults)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rules);
        return new WalkRun(bars, candidate, rules, faults).Execute();
    }
}
