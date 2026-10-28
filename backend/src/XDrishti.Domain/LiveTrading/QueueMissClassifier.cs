using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// Why an entry order got no fill (spec P11-02 decision 5): a limit the market traded through (a buy limit with a 1-minute low below it during the order's life, a sell limit with a high above it) and still did not fill is a <c>queue_miss</c> and counts against
/// capacity; a limit the market never went through is a <c>price_miss</c> (a missed trigger, not a liquidity failure); any order that is not a limit - or has no price or no bars to judge by - is <c>unclassified</c>.
/// </summary>
public static class QueueMissClassifier
{
    /// <summary>The class of an unfilled order. <paramref name="lowestLow"/> and <paramref name="highestHigh"/> are over the 1-minute bars of the order's life; null when there are none.</summary>
    public static string Classify(string orderType, Side side, decimal? limitPrice, decimal? lowestLow, decimal? highestHigh)
    {
        ArgumentNullException.ThrowIfNull(orderType);
        if (!string.Equals(orderType, "LIMIT", StringComparison.OrdinalIgnoreCase) || limitPrice is not { } limit || limit <= 0)
        {
            return OrderFillClass.Unclassified;
        }

        if (side == Side.Buy)
        {
            return lowestLow is { } low ? (low < limit ? OrderFillClass.QueueMiss : OrderFillClass.PriceMiss) : OrderFillClass.Unclassified;
        }

        return highestHigh is { } high ? (high > limit ? OrderFillClass.QueueMiss : OrderFillClass.PriceMiss) : OrderFillClass.Unclassified;
    }

    /// <summary>The class of an entry order from its last revision: filled in full, partly, rejected, or unfilled (judged by the bars).</summary>
    public static string ClassOf(LiveOrder order, decimal? lowestLow, decimal? highestHigh)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.FilledQty >= order.Qty)
        {
            return OrderFillClass.Full;
        }

        if (order.FilledQty > 0)
        {
            return OrderFillClass.Partial;
        }

        return order.Status == LiveOrderStatus.Rejected ? OrderFillClass.Rejected : Classify(order.OrderType, order.Side, order.Price, lowestLow, highestHigh);
    }
}
