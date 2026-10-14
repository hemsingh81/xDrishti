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
            new BrokerTrade("2611250001", "112111182045", TradeSide.Buy, "EXMPL", "1001", 10, 90.5m, new DateOnly(2026, 9, 2), new TimeOnly(10, 15, 2)),
            new BrokerTrade("2611250002", "112111182046", TradeSide.Sell, "SAMPL-EQ", "1002", 3, 130m, new DateOnly(2026, 9, 20), new TimeOnly(14, 2, 12)),
            new BrokerTrade("2611250006", "112111182051", TradeSide.Buy, "CASEY", "1004", 12, 1250.75m, new DateOnly(2026, 9, 23), new TimeOnly(11, 45, 30)),
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
    [InlineData("""[{"exchangeTradeId":"T1","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradedQuantity":1,"tradedPrice":1,"exchangeTime":"2026-09-02 10:00:00"}]""")] // no ticker
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
