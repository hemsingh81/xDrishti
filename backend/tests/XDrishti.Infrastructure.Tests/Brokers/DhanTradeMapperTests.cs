using System.Text.Json;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>Dhan's trade rows into NSE equity trades (ADR 0025, P2-13 criterion 2): the committed sample, segments and products, bad rows, tolerant names and numbers, India time.</summary>
public sealed class DhanTradeMapperTests
{
    private static BrokerTradePage Map(string json) => DhanTradeMapper.Map(JsonDocument.Parse(json).RootElement);

    private static string Row(string segment = "NSE_EQ", string product = "CNC", string extra = "") =>
        $$"""[{"exchangeTradeId":"T1","orderId":"O1","transactionType":"BUY","exchangeSegment":"{{segment}}","productType":"{{product}}","tradingSymbol":"EXMPL","securityId":"1001","tradedQuantity":10,"tradedPrice":90.5,"exchangeTime":"2026-09-02 10:15:02"{{extra}}}]""";

    [Fact]
    public void The_sample_page_gives_the_equity_trades_and_counts_the_rest()
    {
        var page = DhanTradeMapper.Map(DhanSamples.Json("dhan-trades-page.json"));

        page.Rows.ShouldBe(7);
        page.Dropped.ShouldBe(1);
        page.Skipped.ShouldBe(new Dictionary<string, int> { [BrokerSkipReasons.Bse] = 1, [BrokerSkipReasons.Derivatives] = 1, [BrokerSkipReasons.Intraday] = 1 });
        page.Trades.ShouldBe(
        [
            new BrokerTrade("2611250001", "112111182045", TradeSide.Buy, "EXMPL", null, "1001", 10, 90.5m, 0m, new DateOnly(2026, 9, 2), new TimeOnly(10, 15, 2)),
            new BrokerTrade("2611250002", "112111182046", TradeSide.Sell, "SAMPL-EQ", null, "1002", 3, 130m, 0m, new DateOnly(2026, 9, 20), new TimeOnly(14, 2, 12)),
            new BrokerTrade("2611250006", "112111182051", TradeSide.Buy, "CASEY", null, "1004", 12, 1250.75m, 0m, new DateOnly(2026, 9, 23), new TimeOnly(11, 45, 30)),
        ]);
    }

    [Theory]
    [InlineData("BSE_EQ", "CNC", "bse")]
    [InlineData("NSE_FNO", "MARGIN", "derivatives")]
    [InlineData("BSE_FNO", "MARGIN", "derivatives")]
    [InlineData("NSE_CURRENCY", "MARGIN", "currency")]
    [InlineData("BSE_CURRENCY", "MARGIN", "currency")]
    [InlineData("MCX_COMM", "MARGIN", "commodity")]
    [InlineData("IDX_I", "CNC", "non_equity")]
    [InlineData("NSE_EQ", "INTRADAY", "intraday")]
    [InlineData("NSE_EQ", "CO", "intraday")]
    [InlineData("NSE_EQ", "BO", "intraday")]
    [InlineData("nse_eq", "intraday", "intraday")]
    public void Rows_that_are_not_nse_delivery_trades_are_skipped_with_their_reason(string segment, string product, string reason)
    {
        var page = Map(Row(segment, product));

        page.Trades.ShouldBeEmpty();
        page.Dropped.ShouldBe(0);
        page.Skipped.ShouldBe(new Dictionary<string, int> { [reason] = 1 });
    }

    [Theory]
    [InlineData("CNC")]
    [InlineData("MTF")] // margin trading funding is delivery
    [InlineData("MARGIN")] // Dhan's carry-forward product of futures and options; on NSE_EQ it can only be a position carried overnight
    [InlineData("")]
    public void Delivery_products_are_kept(string product)
    {
        Map(Row("NSE_EQ", product)).Trades.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("""[{"transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")] // no trade id
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"HOLD","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")] // no side
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":0,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")] // no quantity
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1.5,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")] // not whole
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":-1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")]
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":0,"exchangeTime":"2026-09-02 10:00:00"}]""")] // no price
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":"abc","exchangeTime":"2026-09-02 10:00:00"}]""")]
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1}]""")] // no time at all
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"yesterday"}]""")]
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1e40,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")] // absurd
    [InlineData("""[5, "x", null]""")]
    public void A_bad_row_is_dropped_and_counted_and_never_skipped(string json)
    {
        var page = Map(json);

        page.Trades.ShouldBeEmpty();
        page.Dropped.ShouldBe(page.Rows);
        page.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public void Dhans_published_row_with_a_null_ticker_is_kept_with_its_isin_and_security_id_and_its_charges_summed()
    {
        var page = DhanTradeMapper.Map(DhanSamples.Json("dhan-published-trade.json"));

        (page.Rows, page.Dropped).ShouldBe((1, 0));
        page.Skipped.ShouldBeEmpty();
        page.Trades.ShouldBe(
        [
            // customSymbol "Tata Motors" is a company name and is not the ticker; createTime and updateTime are "NA", the exchange time is India time.
            new BrokerTrade("407958", "212212307731", TradeSide.Buy, null, "INE155A01022", "3456", 1, 390.9m, 0.0164m, new DateOnly(2022, 12, 30), new TimeOnly(10, 0, 46)),
        ]);
    }

    [Fact]
    public void A_company_name_is_never_taken_as_the_ticker()
    {
        var trade = Map(Row(extra: ",\"customSymbol\":\"Tata Motors\"").Replace("\"tradingSymbol\":\"EXMPL\",", "\"tradingSymbol\":null,", StringComparison.Ordinal)).Trades.ShouldHaveSingleItem();

        trade.Symbol.ShouldBeNull();
        trade.SecurityId.ShouldBe("1001");
    }

    [Theory]
    [InlineData("""{"sebiTax":0.0004,"stt":0,"brokerageCharges":0,"serviceTax":0.0025,"exchangeTransactionCharges":0.0135,"stampDuty":0}""", "0.0164")]
    [InlineData("""{"sebiTax":"0.5","stt":"1.25","brokerageCharges":"20","serviceTax":"3.6","exchangeTransactionCharges":"0.1","stampDuty":"1,000.00"}""", "1025.45")]
    [InlineData("""{"stt":"NA","brokerageCharges":null,"stampDuty":"x","serviceTax":2}""", "2")] // missing, null, "NA" and text that is not a number count as 0
    [InlineData("""{"stt":-5,"brokerageCharges":3}""", "3")] // a negative field does not reduce the total
    [InlineData("""{"stt":0.000049,"brokerageCharges":0.00004}""", "0.0001")] // rounded to 4 decimals
    [InlineData("""{"stt":2000000000}""", "0")] // beyond what a trade's charges can be
    [InlineData("""{}""", "0")]
    public void The_six_charge_fields_are_summed_into_one_total_that_is_never_negative(string charges, string expected)
    {
        var extra = "," + charges.Trim('{', '}');

        var trade = Map(Row(extra: extra.Length > 1 ? extra : string.Empty)).Trades.ShouldHaveSingleItem();

        trade.Charges.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("""{"isin":"INE155A01022","securityId":"3456"}""", "INE155A01022", "3456")]
    [InlineData("""{"isin":"ine155a01022","securityId":"3456"}""", "INE155A01022", "3456")] // the ISIN is upper case
    [InlineData("""{"isin":"NA","securityId":"3456"}""", null, "3456")]
    [InlineData("""{"isin":"not-an-isin","securityId":"3456"}""", null, "3456")]
    [InlineData("""{"isin":"INE155A01022"}""", "INE155A01022", null)]
    public void A_row_without_a_ticker_is_kept_when_it_has_an_isin_or_a_security_id(string identity, string? isin, string? securityId)
    {
        var json = """[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","productType":"CNC","tradingSymbol":null,"tradedQuantity":1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00",""" + identity.Trim('{', '}') + "}]";

        var trade = Map(json).Trades.ShouldHaveSingleItem();

        (trade.Symbol, trade.Isin, trade.SecurityId).ShouldBe((null, isin, securityId));
    }

    [Theory]
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","productType":"CNC","tradingSymbol":null,"customSymbol":"Tata Motors","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")]
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","productType":"CNC","tradingSymbol":"NA","isin":"NA","securityId":"NA","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")]
    public void A_row_with_no_ticker_no_isin_and_no_security_id_is_skipped_and_counted_never_dropped_silently(string json)
    {
        var page = Map(json);

        page.Trades.ShouldBeEmpty();
        page.Dropped.ShouldBe(0);
        page.Skipped.ShouldBe(new Dictionary<string, int> { [BrokerSkipReasons.NoSymbol] = 1 });
    }

    [Fact]
    public void Times_that_are_NA_are_ignored_and_the_next_one_that_reads_is_used()
    {
        var json = """[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"NA","createTime":"NA","updateTime":"2026-09-04 09:20:00"}]""";

        var trade = Map(json).Trades.ShouldHaveSingleItem();

        (trade.Date, trade.Time).ShouldBe((new DateOnly(2026, 9, 4), new TimeOnly(9, 20, 0)));
    }

    [Fact]
    public void A_row_whose_times_are_all_NA_is_dropped()
    {
        var json = """[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"NA","createTime":"NA","updateTime":"NA"}]""";

        Map(json).Dropped.ShouldBe(1);
    }

    [Fact]
    public void An_id_longer_than_the_identity_allows_is_dropped()
    {
        Map(Row().Replace("\"T1\"", "\"" + new string('9', DhanTradeMapper.MaxTradeIdLength + 1) + "\"", StringComparison.Ordinal)).Dropped.ShouldBe(1);
    }

    [Fact]
    public void Names_match_in_any_case_and_numbers_may_be_text_or_have_thousands_separators()
    {
        var page = Map("""[{"EXCHANGETRADEID":"T1","TRANSACTIONTYPE":"sell","EXCHANGESEGMENT":"NSE_EQ","TRADINGSYMBOL":"X","TRADEDQUANTITY":"1,200","TRADEDPRICE":"2,500.5","EXCHANGETIME":"2026-09-02 10:00:00"}]""");

        var trade = page.Trades.ShouldHaveSingleItem();
        (trade.Side, trade.Qty, trade.Price).ShouldBe((TradeSide.Sell, 1200L, 2500.5m));
    }

    [Theory]
    [InlineData("2026-09-02 10:15:02", "2026-09-02", "10:15:02")]
    [InlineData("2026-09-02T10:15:02", "2026-09-02", "10:15:02")]
    [InlineData("2026-09-02 10:15", "2026-09-02", "10:15:00")]
    [InlineData("2026-09-02T04:45:02Z", "2026-09-02", "10:15:02")] // a time with a zone is converted to India time
    [InlineData("2026-09-02T22:00:00Z", "2026-09-03", "03:30:00")]
    [InlineData("2026-09-02", "2026-09-02", null)]
    public void The_time_is_india_time_unless_it_carries_a_zone(string text, string date, string? time)
    {
        var json = $$"""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"{{text}}"}]""";

        var trade = Map(json).Trades.ShouldHaveSingleItem();

        (trade.Date, trade.Time).ShouldBe((DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), time is null ? null : TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void The_create_time_stands_in_for_a_missing_exchange_time()
    {
        var json = """[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"X","tradedQuantity":1,"tradedPrice":1,"createTime":"2026-09-04 09:20:00"}]""";

        Map(json).Trades.Single().Date.ShouldBe(new DateOnly(2026, 9, 4));
    }

    [Fact]
    public void Something_that_is_not_an_array_has_no_rows()
    {
        var page = Map("""{"status":"success"}""");

        (page.Rows, page.Trades.Count, page.Dropped).ShouldBe((0, 0, 0));
    }
}
