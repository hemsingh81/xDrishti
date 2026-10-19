namespace XDrishti.SimOracle;

/// <summary>The results of running a generated case, and the union of the rule rows applied (the trace that is counted).</summary>
public sealed record OracleCaseRun(IReadOnlyList<OracleResult> Results, IReadOnlySet<string> Rows);
