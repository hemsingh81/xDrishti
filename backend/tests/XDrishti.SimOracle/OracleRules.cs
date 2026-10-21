namespace XDrishti.SimOracle;

/// <summary>The rules of one walk. <c>SquareOffMinute</c> is a minute of the day (0 with a hold); <c>Tick</c> is in rupees.</summary>
public sealed record OracleRules(
    double? RewardRisk,
    int SquareOffMinute,
    double EntryLimitPercent,
    OracleSlippage Slippage,
    decimal Tick,
    OracleHold? Hold = null,
    OracleLadder? Ladder = null,
    OracleTrail? Trail = null,
    OraclePolicy? Policy = null);
