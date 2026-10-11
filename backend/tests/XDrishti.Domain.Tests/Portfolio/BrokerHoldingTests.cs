using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

public sealed class BrokerHoldingTests
{
    [Theory]
    [InlineData("INE000A01011", "1001", "ALL", "INE000A01011")]
    [InlineData("ine000a01011 ", "1001", "ALL", "INE000A01011")] // upper case, trimmed
    [InlineData(null, "1001", "nse", "1001:NSE")]
    [InlineData("  ", "1001", "ALL", "1001:ALL")]
    public void The_key_is_the_ISIN_or_the_security_id_with_the_exchange(string? isin, string securityId, string exchange, string expected) =>
        BrokerHolding.KeyOf(isin, securityId, exchange).ShouldBe(expected);

    [Fact]
    public void Rows_of_one_holding_are_added_up_with_a_weighted_average_cost()
    {
        var rows = new[]
        {
            HoldingData.Row(qty: 100, avgCost: 50m, price: 60m),
            HoldingData.Row(qty: 50, avgCost: 80m, price: 61m, securityId: "1002"), // same ISIN, other security id (BSE)
            HoldingData.Row(qty: 10, avgCost: 10m, price: 5m, symbol: "OTHER", securityId: "2002", isin: "INE000B01012"),
        };

        var merged = BrokerHolding.Merge(rows);

        merged.Count.ShouldBe(2);
        var first = merged[0];
        (first.Qty, first.AvgCost, first.LastPrice, first.TradingSymbol).ShouldBe((150L, 60m, 60m, "EXMPL"));
        merged[1].Qty.ShouldBe(10L);
    }

    [Fact]
    public void Rows_without_a_positive_quantity_are_dropped()
    {
        var merged = BrokerHolding.Merge([HoldingData.Row(qty: 0), HoldingData.Row(qty: -5, securityId: "9", isin: null)]);

        merged.ShouldBeEmpty();
    }
}
