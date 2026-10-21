namespace XDrishti.SimOracle;

/// <summary>One fill of a trade with legs (P4-01, D7): the minute, the exact and the filled price, the share of the maximum quantity, whether it is an entry, an add or an exit, and the reason of an exit.</summary>
public sealed record OracleLeg(long EpochSeconds, double Price, double FilledPrice, decimal Share, string Kind, string Reason);
