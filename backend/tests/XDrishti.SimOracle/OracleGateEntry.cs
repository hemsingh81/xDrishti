namespace XDrishti.SimOracle;

/// <summary>What the gate decided for one candidate.</summary>
public sealed record OracleGateEntry(OracleCandidate Candidate, bool Blocked, OracleResult Result, long? HoldEndEpochSeconds);
