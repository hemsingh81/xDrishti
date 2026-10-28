using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criterion 8: a limit the market traded through is a queue miss; one it never reached is a price miss; anything else is unclassified.</summary>
public sealed class OrderClassTests
{
    [Fact]
    public void A_buy_limit_traded_through_is_a_queue_miss_and_one_never_reached_is_a_price_miss()
    {
        QueueMissClassifier.Classify("LIMIT", Side.Buy, 1251.00m, 1250.90m, 1255m).ShouldBe(OrderFillClass.QueueMiss);
        QueueMissClassifier.Classify("LIMIT", Side.Buy, 1251.00m, 1251.05m, 1255m).ShouldBe(OrderFillClass.PriceMiss);
        QueueMissClassifier.Classify("LIMIT", Side.Buy, 1251.00m, 1251.00m, 1255m).ShouldBe(OrderFillClass.PriceMiss); // touched, not traded through
    }

    [Fact]
    public void A_sell_limit_is_judged_by_the_highs()
    {
        QueueMissClassifier.Classify("LIMIT", Side.Sell, 800m, 790m, 800.10m).ShouldBe(OrderFillClass.QueueMiss);
        QueueMissClassifier.Classify("LIMIT", Side.Sell, 800m, 790m, 800m).ShouldBe(OrderFillClass.PriceMiss);
    }

    [Fact]
    public void A_market_order_or_no_price_or_no_bars_is_unclassified()
    {
        QueueMissClassifier.Classify("MARKET", Side.Buy, null, 1m, 2m).ShouldBe(OrderFillClass.Unclassified);
        QueueMissClassifier.Classify("STOP_LOSS", Side.Buy, 10m, 1m, 2m).ShouldBe(OrderFillClass.Unclassified);
        QueueMissClassifier.Classify("LIMIT", Side.Buy, 10m, null, null).ShouldBe(OrderFillClass.Unclassified);
        QueueMissClassifier.Classify("limit", Side.Buy, 10m, 9m, 12m).ShouldBe(OrderFillClass.QueueMiss);
    }

    [Fact]
    public void An_order_is_full_partial_rejected_or_judged_by_the_bars()
    {
        LiveOrder O(string status, long filled, string type = "LIMIT") => LiveFixtureDay.Order("x", "RELIANCE", Side.Buy, 40, status, filled, "09:40:00", 1251m) with { OrderType = type };
        QueueMissClassifier.ClassOf(O("traded", 40), null, null).ShouldBe(OrderFillClass.Full);
        QueueMissClassifier.ClassOf(O("cancelled", 15), null, null).ShouldBe(OrderFillClass.Partial);
        QueueMissClassifier.ClassOf(O("rejected", 0), null, null).ShouldBe(OrderFillClass.Rejected);
        QueueMissClassifier.ClassOf(O("expired", 0), 1250.90m, 1255m).ShouldBe(OrderFillClass.QueueMiss);
        QueueMissClassifier.ClassOf(O("expired", 0, "MARKET"), 1250.90m, 1255m).ShouldBe(OrderFillClass.Unclassified);
    }

    [Fact]
    public void Price_misses_and_unclassified_orders_are_never_counted()
    {
        var counts = CapacityCounts.From([OrderFillClass.Full, OrderFillClass.Partial, OrderFillClass.Rejected, OrderFillClass.QueueMiss, OrderFillClass.PriceMiss, OrderFillClass.Unclassified, OrderFillClass.PriceMiss]);
        counts.Counted.ShouldBe(4);
        counts.PriceMiss.ShouldBe(2);
        counts.Unclassified.ShouldBe(1);
        OrderFillClass.Counted(OrderFillClass.PriceMiss).ShouldBeFalse();
        OrderFillClass.Counted(OrderFillClass.QueueMiss).ShouldBeTrue();
    }
}
