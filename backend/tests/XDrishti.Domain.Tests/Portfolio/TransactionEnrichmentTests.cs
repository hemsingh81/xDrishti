using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>What a later read of the broker's history may complete on a stored broker trade, and nothing else (ADR 0026).</summary>
public sealed class TransactionEnrichmentTests
{
    private static Transaction Trade(TradeSource source = TradeSource.Broker, string? isin = null, string symbol = "EXMPL", decimal charges = 0m) =>
        Transaction.Create(HoldingData.Account, null, isin, symbol, null, TradeSide.Sell, new DateOnly(2024, 3, 5), new TimeOnly(9, 21), 10, 125m, charges, source, null, 7, "t:20240305:9", "owner", HoldingData.T0);

    [Fact]
    public void A_missing_isin_and_zero_charges_are_completed()
    {
        var trade = Trade();

        var changed = trade.EnrichFromBroker("ine000a01011", "EXMPL", 12.34567m);

        (changed, trade.Isin, trade.Charges).ShouldBe((true, "INE000A01011", 12.3457m));
    }

    [Fact]
    public void An_empty_symbol_is_completed_with_the_resolved_one()
    {
        var trade = Trade(isin: "INE000A01011", symbol: string.Empty, charges: 3m);

        var changed = trade.EnrichFromBroker(null, "nse:exmpl-eq", 0m);

        (changed, trade.TradingSymbol).ShouldBe((true, "EXMPL"));
    }

    [Fact]
    public void Nothing_that_is_there_is_overwritten_and_nothing_else_changes()
    {
        var trade = Trade(isin: "INE000A01011", charges: 5m);
        var same = (trade.Id, trade.AccountId, trade.Side, trade.TradeDate, trade.TradeTime, trade.Qty, trade.Price, trade.ExternalId, trade.CreatedAt, trade.CreatedBy, trade.Seq);

        var changed = trade.EnrichFromBroker("INE999Z01019", "OTHER", 99m);

        (changed, trade.Isin, trade.TradingSymbol, trade.Charges).ShouldBe((false, "INE000A01011", "EXMPL", 5m));
        (trade.Id, trade.AccountId, trade.Side, trade.TradeDate, trade.TradeTime, trade.Qty, trade.Price, trade.ExternalId, trade.CreatedAt, trade.CreatedBy, trade.Seq).ShouldBe(same);
    }

    [Fact]
    public void A_read_without_charges_or_isin_completes_nothing()
    {
        var trade = Trade();

        trade.EnrichFromBroker(null, string.Empty, 0m).ShouldBeFalse();
        trade.EnrichFromBroker("  ", "EXMPL", -4m).ShouldBeFalse();

        (trade.Isin, trade.Charges).ShouldBe((null, 0m));
    }

    [Theory]
    [InlineData(TradeSource.Csv)]
    [InlineData(TradeSource.Manual)]
    public void A_trade_from_a_file_or_typed_in_is_left_alone(TradeSource source)
    {
        var trade = Trade(source);

        trade.EnrichFromBroker("INE000A01011", "EXMPL", 12m).ShouldBeFalse();

        (trade.Isin, trade.Charges).ShouldBe((null, 0m));
    }

    [Fact]
    public void A_voided_trade_is_left_alone()
    {
        var trade = Trade();
        trade.Void("owner", HoldingData.T0.AddHours(1));

        trade.EnrichFromBroker("INE000A01011", "EXMPL", 12m).ShouldBeFalse();

        (trade.Isin, trade.Charges).ShouldBe((null, 0m));
    }
}
