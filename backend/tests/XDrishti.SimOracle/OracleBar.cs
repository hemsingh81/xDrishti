namespace XDrishti.SimOracle;

/// <summary>A 1-minute bar; the epoch is the start of the minute. Prices are doubles (rule R1 converts them).</summary>
public sealed record OracleBar(long EpochSeconds, double Open, double High, double Low, double Close, long Volume);
