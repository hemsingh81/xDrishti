namespace XDrishti.SimOracle;

/// <summary>The gate's answer for a candidate list: one entry per candidate, the summed counters, the trace.</summary>
public sealed record OracleGateResult(IReadOnlyList<OracleGateEntry> Entries, OracleCounters Counters, IReadOnlySet<string> Rows)
{
    /// <summary>The positions of the blocked candidates in the input list.</summary>
    public IReadOnlyList<int> BlockedIndexes => [.. Entries.Select((e, i) => (e, i)).Where(x => x.e.Blocked).Select(x => x.i)];
}
