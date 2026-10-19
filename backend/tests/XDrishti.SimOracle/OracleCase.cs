namespace XDrishti.SimOracle;

/// <summary>A generated series: bars, the candidates in decision order (one instrument, one side) and the rules.</summary>
public sealed record OracleCase(
    int Category,
    long Seed,
    IReadOnlyList<OracleBar> Bars,
    IReadOnlyList<OracleCandidate> Candidates,
    OracleRules Rules)
{
    /// <summary>Walks the candidates (through the gate when the rules hold overnight) and collects the trace.</summary>
    public OracleCaseRun Run(OracleFaults faults)
    {
        var rows = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<OracleResult>();
        if (Rules.Hold is not null)
        {
            OracleGateResult g = ReferenceGate.Run(Candidates, c => ReferenceWalker.Walk(Bars, c, Rules, faults));
            foreach (OracleGateEntry e in g.Entries)
            {
                results.Add(e.Result);
            }

            rows.UnionWith(g.Rows);
        }
        else
        {
            foreach (OracleCandidate c in Candidates)
            {
                OracleResult r = ReferenceWalker.Walk(Bars, c, Rules, faults);
                results.Add(r);
                rows.UnionWith(r.Rows);
            }
        }

        return new OracleCaseRun(results, rows);
    }
}
