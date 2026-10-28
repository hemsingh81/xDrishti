using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// Spec P11-01 criterion 1: the trade book and order book mappers against the samples under Brokers/Samples. LIMIT: those answers are made up from Dhan's public documentation (no real answer of the owner's account exists yet, the first task of
/// the slice is the owner's act); a real, scrubbed answer replaces them when the owner supplies it.
/// </summary>
public sealed class DhanBookMappersTests
{
    [Fact]
    public void Every_product_of_the_cash_segment_is_kept_with_its_product_other_segments_are_skipped_per_reason_and_bad_rows_dropped_by_reason()
    {
        var read = DhanTradeBookMapper.Map(DhanSamples.Json("dhan-trade-book-day.json"));

        read.Seen.ShouldBe(12);
        read.Rows.Select(f => (f.TradeId, f.Product, f.Qty)).ShouldBe([("T-0001", "CNC", 25L), ("T-0002", "CNC", 15L), ("T-0003", "INTRADAY", 100L)]);
        read.Skipped.ShouldBe(new Dictionary<string, int> { [BrokerSkipReasons.Bse] = 1, [BrokerSkipReasons.Derivatives] = 1, [BrokerSkipReasons.NoSymbol] = 1 });
        read.Dropped.ShouldBe(6);
        read.Drops.ByReason.ShouldBe(new Dictionary<string, int>
        {
            [BrokerDropReasons.BadPrice] = 1,
            [BrokerDropReasons.BadQuantity] = 1,
            [BrokerDropReasons.BadTime] = 1,
            [BrokerDropReasons.MissingId] = 1,
            [BrokerDropReasons.MissingSide] = 1,
            [BrokerDropReasons.UnreadableRow] = 1,
        });
    }

    [Fact]
    public void A_row_reads_with_text_numbers_NA_times_and_any_case_and_has_every_field_the_matcher_needs()
    {
        var second = DhanTradeBookMapper.Map(DhanSamples.Json("dhan-trade-book-day.json")).Rows[1];

        (second.Side, second.Qty, second.Price, second.Date, second.Time, second.OrderId, second.Symbol).ShouldBe(
            (Side.Buy, 15L, 1250.60m, new DateOnly(2026, 11, 3), new TimeOnly(9, 41, 30), "2611030001", "RELIANCE"));
    }

    [Fact]
    public void The_order_book_keeps_the_seven_states_skips_other_segments_and_drops_the_rest_by_reason()
    {
        var read = DhanOrderBookMapper.Map(DhanSamples.Json("dhan-order-book-day.json"));

        read.Seen.ShouldBe(10);
        read.Rows.Select(o => (o.OrderId, o.Status, o.FilledQty)).ShouldBe(
        [
            ("2611030001", "traded", 40L), ("2611030002", "pending", 0L), ("2611030003", "rejected", 0L), ("2611030004", "part_traded", 12L), ("2611030005", "traded", 100L),
        ]);
        read.Skipped.ShouldBe(new Dictionary<string, int> { [BrokerSkipReasons.Derivatives] = 1 });
        read.Drops.ByReason.ShouldBe(new Dictionary<string, int>
        {
            [BrokerDropReasons.BadQuantity] = 1,
            [BrokerDropReasons.BadTime] = 1,
            [BrokerDropReasons.MissingId] = 1,
            [BrokerDropReasons.Other] = 1, // TRIGGERED is not one of the seven
        });
    }

    [Fact]
    public void A_stop_order_reads_its_trigger_and_a_market_order_has_no_price()
    {
        var orders = DhanOrderBookMapper.Map(DhanSamples.Json("dhan-order-book-day.json")).Rows;

        var stop = orders[1];
        (stop.Side, stop.OrderType, stop.TriggerPrice, stop.Price, stop.Qty, stop.CreateTime.TimeOfDay).ShouldBe((Side.Sell, "STOP_LOSS_MARKET", 1240m, null, 40L, new TimeSpan(9, 44, 2)));
        orders[4].Price.ShouldBeNull();
        orders[0].TriggerPrice.ShouldBeNull();
    }

    [Fact]
    public void Broker_text_is_capped_stripped_of_control_characters_and_kept_as_data()
    {
        var rejected = DhanOrderBookMapper.Map(DhanSamples.Json("dhan-order-book-day.json")).Rows[2];

        rejected.RejectText.ShouldBe("insufficient margin <script>alert(1)</script>"); // stored as text; the screen renders it as text
        DhanBookText.Cap(new string('x', 500), 200)!.Length.ShouldBe(200);
        DhanBookText.Cap("  \u0001\u0002 ", 10).ShouldBeNull();
    }

    [Fact]
    public void The_seven_states_are_the_set_of_the_table()
    {
        LiveOrderStatus.All.Count.ShouldBe(7);
        LiveOrderStatus.Parse("PART_TRADED").ShouldBe("part_traded");
        LiveOrderStatus.Parse("CLOSED").ShouldBeNull();
    }

    [Fact]
    public void The_two_mappers_keep_nothing_of_a_row_in_their_counts()
    {
        var text = string.Join('|', DhanTradeBookMapper.Map(DhanSamples.Json("dhan-trade-book-day.json")).Drops.Rows.Select(r => r.Reason));

        text.ShouldNotContain("RELIANCE");
        text.ShouldNotContain("INFY");
    }
}
