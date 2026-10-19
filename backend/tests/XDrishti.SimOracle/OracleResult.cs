namespace XDrishti.SimOracle;

/// <summary>The oracle's answer for one candidate: its own trade or the no-trade reason, its own counters and the rule rows it applied.</summary>
public sealed record OracleResult(
    OracleTrade? Trade,
    OracleNoTradeReason? NoTrade,
    OracleCounters Counters,
    IReadOnlySet<string> Rows,
    long? HoldEndEpochSeconds);
