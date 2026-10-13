using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeScenario;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>Adding trades by hand, listing a holding's trades and the import history (ADR 0023; P2-03 criteria 14, 15).</summary>
public sealed class TradeManualAndListTests
{
    private static ManualTradeInput Input(TradeScenario s, string side = "buy", string date = "2024-03-05", decimal quantity = 10, decimal price = 90m, decimal? charges = null) =>
        new(null, s.Holding.Id, null, null, side, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), null, quantity, price, charges);

    private static Task<XDrishti.Domain.Common.Result<TradeView>> AddAsync(TradeScenario s, ManualTradeInput input) =>
        s.Fx.AddManualTrade().HandleAsync(new AddManualTradeCommand(input, Actor), CancellationToken.None).AsTask();

    [Fact]
    public async Task A_manual_trade_for_a_holding_is_stored_as_a_manual_trade_of_the_holdings_security_and_changes_no_lot()
    {
        var s = await StartAsync();
        var lots = Shape(s.Holding);

        var trade = Ok(await AddAsync(s, Input(s, charges: 12.5m)));

        (trade.Side, trade.Source, trade.Symbol, trade.Isin, trade.Qty, trade.Price, trade.Charges, trade.BatchId, trade.Voided).ShouldBe(("buy", "manual", "EXMPL", Isin, 10L, 90m, 12.5m, null, false));
        s.Data.Trades.ShouldHaveSingleItem().ExternalId.ShouldStartWith("m:");
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Events(AccountEventType.TradeAdded).ShouldHaveSingleItem().Detail.ShouldBe("Trade added by hand (buy)");
    }

    [Fact]
    public async Task Identical_manual_trades_are_both_kept()
    {
        var s = await StartAsync();

        Ok(await AddAsync(s, Input(s)));
        Ok(await AddAsync(s, Input(s)));

        s.Data.Trades.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_manual_trade_counts_in_the_next_rebuild()
    {
        var s = await StartAsync();
        Ok(await AddAsync(s, Input(s, quantity: 10, price: 90m)));

        var rebuild = Ok(await s.RebuildAsync(dryRun: false));

        (rebuild.Applied, rebuild.Rebuild.Status, rebuild.Rebuild.Net, rebuild.Rebuild.LotsAfter).ShouldBe((true, "ok", 10L, 2));
        s.Holding.Lots.Single(l => l.Source == XDrishti.Domain.Portfolio.LotSource.Trade).UnitCost.ShouldBe(90m);
    }

    [Theory]
    [InlineData("hold", "2024-03-05", 10, 90, "side")]
    [InlineData("buy", "2026-10-11", 10, 90, "tradeDate")]
    [InlineData("buy", "1989-01-01", 10, 90, "tradeDate")]
    [InlineData("buy", "2024-03-05", 1.5, 90, "quantity")]
    [InlineData("buy", "2024-03-05", 0, 90, "quantity")]
    [InlineData("buy", "2024-03-05", 10, 0, "price")]
    [InlineData("buy", "2024-03-05", 10, -1, "price")]
    public async Task A_manual_trade_is_validated_like_a_csv_row(string side, string date, double quantity, double price, string field)
    {
        var s = await StartAsync();

        var result = await AddAsync(s, Input(s, side, date, (decimal)quantity, (decimal)price));

        result.Error.Code.ShouldBe("portfolio.invalid");
        FieldsOf(result).ShouldContain(field);
        s.Data.Trades.ShouldBeEmpty();
    }

    [Fact]
    public async Task Negative_charges_and_missing_values_are_refused()
    {
        var s = await StartAsync();

        FieldsOf(await AddAsync(s, Input(s, charges: -1m))).ShouldContain("charges");
        FieldsOf(await AddAsync(s, new ManualTradeInput(null, s.Holding.Id, null, null, null, null, null, null, null, null))).ShouldBe(["side", "tradeDate", "quantity", "price"]);
    }

    [Fact]
    public async Task A_trade_for_a_security_of_an_account_needs_an_nse_share_or_etf_of_the_master_list()
    {
        var s = await StartAsync();
        var typo = new ManualTradeInput(s.Account.Id, null, "ACMEE", null, "buy", new DateOnly(2024, 3, 5), null, 10, 90m, null);

        var refused = await AddAsync(s, typo);
        s.Data.Instruments.Add(new TradeInstrument(Guid.NewGuid(), "ACME", "INE000B01019", "share"));
        var accepted = Ok(await AddAsync(s, typo with { Symbol = "nse:acme-eq" }));

        FieldsOf(refused).ShouldBe(["symbol"]);
        (accepted.Symbol, accepted.Isin).ShouldBe(("ACME", "INE000B01019"));
        s.Data.Trades.Single().InstrumentId.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_account_must_match_the_holding_and_the_holding_must_exist()
    {
        var s = await StartAsync();

        var mismatch = await AddAsync(s, Input(s) with { AccountId = Guid.NewGuid() });
        var unknown = await AddAsync(s, Input(s) with { HoldingId = Guid.NewGuid() });
        var neither = await AddAsync(s, Input(s) with { HoldingId = null });

        FieldsOf(mismatch).ShouldBe(["accountId"]);
        FieldsOf(unknown).ShouldBe(["holdingId"]);
        FieldsOf(neither).ShouldContain("accountId");
    }

    [Fact]
    public async Task The_trades_of_a_holding_come_newest_first_with_voided_ones_flagged_and_in_pages()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows), rebuild: false);
        Ok(await s.Fx.VoidTrade().HandleAsync(new VoidTradeCommand(s.Data.Trades.Single(t => t.ExternalId == "t:20240612:T2").Id, Actor), CancellationToken.None));
        Ok(await AddAsync(s, Input(s, date: "2024-12-01")));

        var page = Ok(await s.Fx.ListHoldingTrades().HandleAsync(new ListHoldingTradesQuery(s.Holding.Id, null, null), CancellationToken.None));
        var first = Ok(await s.Fx.ListHoldingTrades().HandleAsync(new ListHoldingTradesQuery(s.Holding.Id, 1, 2), CancellationToken.None));
        var second = Ok(await s.Fx.ListHoldingTrades().HandleAsync(new ListHoldingTradesQuery(s.Holding.Id, 2, 2), CancellationToken.None));

        page.Total.ShouldBe(4);
        page.Items.Select(t => (t.TradeDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), t.Voided)).ShouldBe(
            [("2024-12-01", false), ("2024-09-20", false), ("2024-06-12", true), ("2024-03-05", false)]);
        (first.Items.Count, second.Items.Count, second.Total, second.Page).ShouldBe((2, 2, 4, 2));
    }

    [Fact]
    public async Task Listing_validates_the_page_and_knows_the_holding()
    {
        var s = await StartAsync();

        var bad = await s.Fx.ListHoldingTrades().HandleAsync(new ListHoldingTradesQuery(s.Holding.Id, 0, 500), CancellationToken.None);
        var unknown = await s.Fx.ListHoldingTrades().HandleAsync(new ListHoldingTradesQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        FieldsOf(bad).ShouldBe(["page", "pageSize"]);
        unknown.Error.Code.ShouldBe("portfolio.not_found");
    }

    [Fact]
    public async Task The_import_history_is_newest_first_in_pages_with_the_account_name_and_the_counts()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows[0]), rebuild: false);
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(5));
        await s.ImportOkAsync(Csv(BaseRows[1]), rebuild: false);

        var page = Ok(await s.Fx.ListTradeBatches().HandleAsync(new ListTradeBatchesQuery(s.Account.Id, 1, 1), CancellationToken.None));
        var all = Ok(await s.Fx.ListTradeBatches().HandleAsync(new ListTradeBatchesQuery(null, null, null), CancellationToken.None));

        (page.Items.Count, page.Total).ShouldBe((1, 2));
        all.Items.Select(b => b.Imported).ShouldBe([1, 1]);
        all.Items[0].CreatedAt.ShouldBeGreaterThan(all.Items[1].CreatedAt);
        (all.Items[0].AccountName, all.Items[0].ActiveTrades, all.Items[0].Securities).ShouldBe((s.Account.Name, 1, 1));
        FieldsOf(await s.Fx.ListTradeBatches().HandleAsync(new ListTradeBatchesQuery(null, 0, 1000), CancellationToken.None)).ShouldBe(["page", "pageSize"]);
    }

    [Fact]
    public async Task The_template_is_the_readers_template()
    {
        var csv = await new GetTradeTemplateHandler().HandleAsync(new GetTradeTemplateQuery(), CancellationToken.None);

        csv.ShouldBe(TradeTemplate.Csv);
        csv.ShouldStartWith("Date,Symbol,ISIN,Side,Quantity,Price,Exchange,Trade ID,Time,Charges");
    }
}
