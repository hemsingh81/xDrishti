namespace XDrishti.SimOracle;

/// <summary>One-line deviations of the cost reference, for test use only: each one is switchable alone.</summary>
[Flags]
public enum OracleCostFaults
{
    None = 0,
    DpChargeOnBuy = 1 << 0,
    GstOnWrongBase = 1 << 1,
    RoundingPerTrade = 1 << 2,
    SttOnBothSides = 1 << 3,
    StampDutyOnSell = 1 << 4,
    BrokerageCapIgnored = 1 << 5,
    QuantityFromFilledEntry = 1 << 6,
    FloorReplacedByRound = 1 << 7,
    CostRByBudget = 1 << 8,
    HalfEvenInsteadOfHalfAway = 1 << 9,
    ExitLegUsesEntrySchedule = 1 << 10,
    CostEqualToLimitSkipped = 1 << 11,
    NetRFromExactR = 1 << 12,
}
