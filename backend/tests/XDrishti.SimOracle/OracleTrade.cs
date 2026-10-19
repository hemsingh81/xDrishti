namespace XDrishti.SimOracle;

/// <summary>The oracle's own trade; prices and ratios are stored doubles (rule R1).</summary>
public sealed record OracleTrade(
    OracleSide Side,
    DateOnly TradeDate,
    long DecisionEpochSeconds,
    long EntryEpochSeconds,
    double EntryPrice,
    double StopPrice,
    double? TargetPrice,
    long ExitEpochSeconds,
    double ExitPrice,
    OracleExitReason ExitReason,
    double RExact,
    double MaeR,
    double MfeR,
    OracleFillBasis Fills,
    double RFilled,
    double SlippageR,
    double EntryFilledPrice,
    double ExitFilledPrice,
    OracleEntryFill EntryFill,
    int SessionsHeld,
    int SessionsUnseen);
