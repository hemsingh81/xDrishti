namespace XDrishti.SimOracle;

/// <summary>The categories of the synthetic cost cases; each is counted by the trace of the reference.</summary>
public enum OracleCostCategory
{
    IntradaySegment,
    DeliverySegment,
    RoundHalfAway,
    RoundHalfEven,
    RoundDown,
    BrokerageAtMin,
    BrokerageAtCap,
    BrokerageBetween,
    QuantityZero,
    CostOverLimit,
    SellTrade,
    BuyTrade,
    DateChangeBetweenLegs,
}
