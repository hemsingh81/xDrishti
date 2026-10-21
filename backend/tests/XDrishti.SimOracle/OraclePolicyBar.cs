namespace XDrishti.SimOracle;

/// <summary>One closed bar of the evaluation timeframe as the P4-02 policies read it: the instant it closed (the end of its last minute), its close and the ATR as of that close; a NaN is a value that was not ready.</summary>
public sealed record OraclePolicyBar(long CloseEpochSeconds, double Close, double Atr);
