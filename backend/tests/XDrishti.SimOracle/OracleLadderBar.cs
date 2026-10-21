namespace XDrishti.SimOracle;

/// <summary>One closed bar of the evaluation timeframe: the instant it closed (the end of its last minute) and its close, low and high; a NaN is a value that was not ready.</summary>
public sealed record OracleLadderBar(long CloseEpochSeconds, double Close, double Low, double High);
