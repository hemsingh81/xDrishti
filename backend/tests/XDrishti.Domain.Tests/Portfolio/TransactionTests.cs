using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>A trade and an import batch (ADR 0023): what they guard, and the rules for trade values.</summary>
public sealed class TransactionTests
{
    private static readonly Guid Account = HoldingData.Account;

    private static Transaction Trade(
        string symbol = "nse:exmpl-eq", string? isin = "ine000a01011", long qty = 10, decimal price = 1250.123456m, decimal charges = 12.34567m, string externalId = "t:1", Guid? batch = null) =>
        Transaction.Create(Account, null, isin, symbol, null, TradeSide.Buy, new DateOnly(2024, 3, 5), new TimeOnly(9, 21), qty, price, charges, TradeSource.Csv, batch, 7, externalId, " owner@example.com ", HoldingData.T0);

    [Fact]
    public void A_trade_normalises_its_symbol_isin_and_exchange_and_keeps_the_price_to_six_decimals_and_the_charges_to_four()
    {
        var trade = Trade();

        (trade.TradingSymbol, trade.Isin, trade.Exchange, trade.Price, trade.Charges, trade.CreatedBy, trade.Seq, trade.IsVoided).ShouldBe(("EXMPL", "INE000A01011", "NSE", 1250.123456m, 12.3457m, "owner@example.com", 7, false));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(-1, 10, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, -5, 0)]
    [InlineData(1, 10, -1)]
    public void A_trade_has_a_positive_quantity_and_price_and_non_negative_charges(long qty, decimal price, decimal charges)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Trade(qty: qty, price: price, charges: charges));
    }

    [Fact]
    public void A_trade_needs_a_symbol_or_an_isin_an_account_and_an_identity()
    {
        Should.Throw<ArgumentException>(() => Trade(symbol: " ", isin: null));
        Should.Throw<ArgumentException>(() => Trade(externalId: ""));
        Should.Throw<ArgumentException>(() => Trade(externalId: new string('x', 101)));
        Trade(symbol: "", isin: "INE000A01011").TradingSymbol.ShouldBeEmpty();
    }

    [Fact]
    public void A_trade_is_voided_once()
    {
        var trade = Trade();

        var first = trade.Void("owner@example.com", HoldingData.T0.AddHours(1));
        var second = trade.Void("someone else", HoldingData.T0.AddHours(2));

        (first, second, trade.IsVoided, trade.VoidedBy, trade.VoidedAt).ShouldBe((true, false, true, "owner@example.com", HoldingData.T0.AddHours(1)));
    }

    [Fact]
    public void A_batch_starts_imported_and_is_undone_once()
    {
        var batch = TradeBatch.Start(Guid.CreateVersion7(), Account, @"C:\Users\me\tradebook.csv", new string('A', 64), 10, 8, 2, 3, 1, "owner@example.com", HoldingData.T0);

        var first = batch.Undo(HoldingData.T0.AddHours(1));
        var second = batch.Undo(HoldingData.T0.AddHours(2));

        (batch.FileName, batch.Sha256, batch.Status, batch.UndoneAt, first, second).ShouldBe(("tradebook.csv", new string('a', 64), TradeBatchStatus.Undone, HoldingData.T0.AddHours(1), true, false));
    }

    [Fact]
    public void A_batch_needs_a_hash_and_counts_that_are_not_negative_and_cleans_its_file_name()
    {
        Should.Throw<ArgumentException>(() => TradeBatch.Start(Guid.CreateVersion7(), Account, "a.csv", "abc", 1, 1, 0, 0, 0, "o", HoldingData.T0));
        Should.Throw<ArgumentOutOfRangeException>(() => TradeBatch.Start(Guid.CreateVersion7(), Account, "a.csv", new string('a', 64), -1, 1, 0, 0, 0, "o", HoldingData.T0));
        TradeBatch.Start(Guid.CreateVersion7(), Account, "x\u0007y\n.csv", new string('a', 64), 1, 1, 0, 0, 0, "o", HoldingData.T0).FileName.ShouldBe("xy.csv");
        TradeBatch.Start(Guid.CreateVersion7(), Account, "  ", new string('a', 64), 1, 1, 0, 0, 0, "o", HoldingData.T0).FileName.ShouldBe("trades.csv");
    }

    [Theory]
    [InlineData("buy", true, TradeSide.Buy)]
    [InlineData("B", true, TradeSide.Buy)]
    [InlineData(" Purchase ", true, TradeSide.Buy)]
    [InlineData("SELL", true, TradeSide.Sell)]
    [InlineData("s", true, TradeSide.Sell)]
    [InlineData("sale", true, TradeSide.Sell)]
    [InlineData("hold", false, TradeSide.Buy)]
    [InlineData("", false, TradeSide.Buy)]
    [InlineData(null, false, TradeSide.Buy)]
    public void Sides_have_their_aliases(string? text, bool parsed, TradeSide side)
    {
        TradeRules.TryParseSide(text, out var result).ShouldBe(parsed);
        if (parsed)
        {
            result.ShouldBe(side);
        }
    }

    [Theory]
    [InlineData("infy", "INFY")]
    [InlineData(" NSE:Infy-EQ ", "INFY")]
    [InlineData("-EQ", "-EQ")]
    [InlineData("M&M", "M&M")]
    [InlineData("", "")]
    public void Symbols_lose_their_prefix_and_series_suffix(string text, string expected)
    {
        TradeRules.NormaliseSymbol(text).ShouldBe(expected);
    }

    [Fact]
    public void The_date_of_a_trade_is_between_1990_01_02_and_today()
    {
        var errors = new FieldErrors();
        var today = new DateOnly(2026, 10, 13);

        TradeRules.CheckDate(errors, "a", today, today);
        TradeRules.CheckDate(errors, "b", today.AddDays(1), today);
        TradeRules.CheckDate(errors, "c", new DateOnly(1990, 1, 1), today);
        TradeRules.CheckDate(errors, "d", new DateOnly(1990, 1, 2), today);

        errors.Items.Select(e => e.Field).ShouldBe(["b", "c"]);
    }
}
