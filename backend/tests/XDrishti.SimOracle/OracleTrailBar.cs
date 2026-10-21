namespace XDrishti.SimOracle;

/// <summary>One closed bar of the evaluation timeframe for a trail: the instant it closed (the end of its last minute), its close and the value the rule reads; a NaN is a value that was not ready.</summary>
public sealed record OracleTrailBar(long CloseEpochSeconds, double Close, double Value);
