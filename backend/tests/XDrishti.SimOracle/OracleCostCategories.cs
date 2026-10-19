namespace XDrishti.SimOracle;

/// <summary>The trace id that proves a category was reached.</summary>
public static class OracleCostCategories
{
    /// <summary>The rule-row id a case of the category must carry in its trace.</summary>
    public static string TraceId(OracleCostCategory category) => category switch
    {
        OracleCostCategory.IntradaySegment => CostTrace.Intraday,
        OracleCostCategory.DeliverySegment => CostTrace.Delivery,
        OracleCostCategory.RoundHalfAway => CostTrace.RoundAway,
        OracleCostCategory.RoundHalfEven => CostTrace.RoundEven,
        OracleCostCategory.RoundDown => CostTrace.RoundDown,
        OracleCostCategory.BrokerageAtMin => CostTrace.BrokerageMin,
        OracleCostCategory.BrokerageAtCap => CostTrace.BrokerageCap,
        OracleCostCategory.BrokerageBetween => CostTrace.BrokerageBetween,
        OracleCostCategory.QuantityZero => CostTrace.QuantityZero,
        OracleCostCategory.CostOverLimit => CostTrace.CostSkip,
        OracleCostCategory.SellTrade => CostTrace.TradeSell,
        OracleCostCategory.BuyTrade => CostTrace.TradeBuy,
        _ => CostTrace.DateChange,
    };
}
