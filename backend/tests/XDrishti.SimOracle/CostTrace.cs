namespace XDrishti.SimOracle;

/// <summary>The rule-row ids of the cost reference's trace.</summary>
internal static class CostTrace
{
    internal const string Intraday = "C-SEG-INTRADAY";
    internal const string Delivery = "C-SEG-DELIVERY";
    internal const string RoundAway = "C-ROUND-AWAY";
    internal const string RoundEven = "C-ROUND-EVEN";
    internal const string RoundDown = "C-ROUND-DOWN";
    internal const string BrokerageMin = "C-BROK-MIN";
    internal const string BrokerageCap = "C-BROK-CAP";
    internal const string BrokerageBetween = "C-BROK-BETWEEN";
    internal const string QuantityZero = "C-QTY-ZERO";
    internal const string CostSkip = "C-COST-SKIP";
    internal const string LegBuy = "C-LEG-BUY";
    internal const string LegSell = "C-LEG-SELL";
    internal const string TradeBuy = "C-TRADE-BUY";
    internal const string TradeSell = "C-TRADE-SELL";
    internal const string DateChange = "C-DATE-CHANGE";
}
