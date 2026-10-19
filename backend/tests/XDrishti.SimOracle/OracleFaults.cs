namespace XDrishti.SimOracle;

/// <summary>One-line deviations of the walker, for test use only: each one is switchable alone.</summary>
[Flags]
public enum OracleFaults
{
    None = 0,
    FillOnSignalMinute = 1 << 0,
    StopCheckedAfterTarget = 1 << 1,
    GapFilledAtStopPrice = 1 << 2,
    OffByOneMinute = 1 << 3,
    BuyTargetFilledAtOpenWhenBeyond = 1 << 4,
    LimitRoundedToNearestTick = 1 << 5,
    SlippageOnTargetExit = 1 << 6,
    ExtraStopTicksOnMaxHold = 1 << 7,
    EntrySlippedPastLimit = 1 << 8,
    MaeFromFilledEntry = 1 << 9,
    AtLimitHighCountedInMfe = 1 << 10,
    ZeroVolumeMinuteIsTrade = 1 << 11,
    MaxHoldFromDecisionDate = 1 << 12,
    WeekendNotSkippedInE9 = 1 << 13,
    Minute0921AllowedAsFill = 1 << 14,
    PendingReadAsNoFillBar = 1 << 15,
}
