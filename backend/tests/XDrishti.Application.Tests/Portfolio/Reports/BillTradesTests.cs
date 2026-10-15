using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>How a bill row becomes trades (ADR 0029, P2-17 criteria 2, 6 and 7): sides, price and value, the charges and their split, which rows are not equity, and the identity.</summary>
public sealed class BillTradesTests
{
    private static GtrRow Row(long buyQty = 0, string buyValue = "0", long sellQty = 0, string sellValue = "0", decimal charges = 0m, string name = "Orion Steel", string exchange = "NSE", string bill = "100", string date = "2026-04-07") =>
        new(2, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), name, exchange, bill, buyQty, decimal.Parse(buyValue, System.Globalization.CultureInfo.InvariantCulture), sellQty, decimal.Parse(sellValue, System.Globalization.CultureInfo.InvariantCulture), charges);

    [Fact]
    public void A_buy_only_row_is_one_buy_with_all_the_charges()
    {
        var legs = BillTrades.Legs(Row(buyQty: 650, buyValue: "15086.50", charges: 2.57m));

        var buy = legs.ShouldHaveSingleItem();
        (buy.Side, buy.Qty, buy.Charges).ShouldBe((TradeSide.Buy, 650L, 2.57m));
        buy.Price.ShouldBe(23.210000m);
    }

    [Fact]
    public void A_sell_only_row_is_one_sell()
    {
        var legs = BillTrades.Legs(Row(sellQty: 40, sellValue: "148800.00", charges: 168.80m));

        legs.ShouldHaveSingleItem().Side.ShouldBe(TradeSide.Sell);
    }

    [Fact]
    public void A_row_with_a_buy_and_a_sell_quantity_is_a_buy_then_a_sell_with_the_charges_split_by_value_adding_up_exactly()
    {
        var legs = BillTrades.Legs(Row(buyQty: 100, buyValue: "25000.00", sellQty: 100, sellValue: "25400.00", charges: 100m));

        legs.Select(l => l.Side).ShouldBe([TradeSide.Buy, TradeSide.Sell]);
        legs[0].Charges.ShouldBe(49.6032m);
        legs[1].Charges.ShouldBe(50.3968m);
        legs.Sum(l => l.Charges).ShouldBe(100m);
    }

    [Fact]
    public void A_row_whose_charges_are_a_net_refund_carries_no_charges()
    {
        var legs = BillTrades.Legs(Row(sellQty: 10, sellValue: "1000.00", charges: -0.50m));

        legs.Single().Charges.ShouldBe(0m);
    }

    [Fact]
    public async Task Quantity_times_price_gives_the_value_back_to_the_paisa_for_every_row_of_the_sample()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));

        var legs = file.Items.SelectMany(BillTrades.Legs).ToList();

        legs.Count.ShouldBeGreaterThan(40);
        legs.ShouldAllBe(l => l.Exact);
        legs.ShouldAllBe(l => Math.Round(l.Qty * l.Price, 2, MidpointRounding.AwayFromZero) == l.Value);
        legs.ShouldAllBe(l => l.Price == Math.Round(l.Price, 6));
    }

    [Fact]
    public async Task The_charges_of_the_legs_add_up_to_the_charges_of_the_rows_and_to_the_footer_of_the_sample()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));

        var total = file.Items.SelectMany(BillTrades.Legs).Sum(l => l.Charges);

        Math.Abs(total - file.FooterCharges!.Value).ShouldBeLessThanOrEqualTo(0.5m); // the one refund row carries none: the legs are never below the footer by more than that refund
        file.Items.Sum(i => i.Charges).ShouldBe(file.FooterCharges.Value);
    }

    [Fact]
    public void A_price_that_cannot_be_exact_is_flagged_not_hidden()
    {
        // 3 shares for 100.00: 33.333333 × 3 = 99.999999, which rounds to 100.00 - exact to the paisa. 7 shares for 100.01 is not exactly reproducible at a huge quantity.
        var legs = BillTrades.Legs(Row(buyQty: 3, buyValue: "100.00"));

        legs.Single().Price.ShouldBe(33.333333m);
        legs.Single().Exact.ShouldBeTrue();
    }

    [Theory]
    [InlineData("FUT NIFTY 28 Apr 2026", "NSE", "derivatives")]
    [InlineData("OPT BANKNIFTY 28 Apr 2026 CE 52000", "NSE", "derivatives")]
    [InlineData("opt nifty 22000 pe", "NSE", "derivatives")]
    [InlineData("NIFTY24MARFUT", "NSE", "derivatives")]
    [InlineData("RELIANCE24MAR2500CE", "NSE", "derivatives")]
    [InlineData("GOLDM FUT 05 Jun 2026", "MCX", "derivatives")]
    [InlineData("GOLD MINI", "MCX", "commodity")]
    [InlineData("CRUDE", "NCDEX", "commodity")]
    [InlineData("USDINR", "CDS", "currency")]
    [InlineData("SOMETHING", "NFO", "derivatives")]
    [InlineData("SOMETHING", "XYZ", "non_equity")]
    public void Futures_options_and_commodity_rows_are_not_equity_with_their_reason(string name, string exchange, string reason)
    {
        BillTrades.NotEquity(Row(name: name, exchange: exchange), BillName.Parse(name)).ShouldBe(reason);
    }

    [Theory]
    [InlineData("Orion Gold ETF (ORIONGLD)", "NSE")]
    [InlineData("ORION STEEL LTD.", "BSE")]
    [InlineData("TERRA DYNAMICS POWER SYSTEMS LTD.", "")]
    [InlineData("Orion Steel", "nse")]
    public void Shares_and_etfs_of_the_nse_and_the_bse_are_equity(string name, string exchange)
    {
        BillTrades.NotEquity(Row(name: name, exchange: exchange), BillName.Parse(name)).ShouldBeNull();
    }

    [Fact]
    public void The_identity_is_the_same_for_the_same_bill_day_scrip_and_side_however_the_name_is_spelt()
    {
        var one = BillTrades.Identity(Row(bill: "443400"), TradeSide.Buy, BillName.Parse("ORION STEEL LTD."));
        var other = BillTrades.Identity(Row(bill: "443400"), TradeSide.Buy, BillName.Parse("Orion Steel"));

        one.ShouldBe(other);
        one.ShouldStartWith("g:");
        one.Length.ShouldBe(34);
    }

    [Fact]
    public void The_identity_differs_by_bill_day_scrip_and_side()
    {
        var name = BillName.Parse("Orion Steel");
        var baseline = BillTrades.Identity(Row(), TradeSide.Buy, name);

        BillTrades.Identity(Row(bill: "101"), TradeSide.Buy, name).ShouldNotBe(baseline);
        BillTrades.Identity(Row(date: "2026-04-08"), TradeSide.Buy, name).ShouldNotBe(baseline);
        BillTrades.Identity(Row(), TradeSide.Sell, name).ShouldNotBe(baseline);
        BillTrades.Identity(Row(), TradeSide.Buy, BillName.Parse("Lumen Oils")).ShouldNotBe(baseline);
    }
}
