using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeScenario;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>What owning lots rebuilt from trades means for the rest of the Portfolio module (ADR 0023; P2-03 criteria 15, 16): the dates of trade lots, the opening lot, the first-import re-dating, the rebuild of one holding.</summary>
public sealed class TradeLotRulesTests
{
    private static async Task<Result<LotRow>> SetDateAsync(TradeScenario s, HoldingLot lot, DateOnly date) =>
        await s.Fx.UpdateLotDate().HandleAsync(new UpdateLotDateCommand(s.Holding.Id, lot.Id, new LotDateInput(date), Actor), CancellationToken.None);

    [Fact]
    public async Task The_date_of_a_lot_made_from_a_trade_is_read_only()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        var lot = s.Holding.Lots.First(l => l.Source == LotSource.Trade);

        var result = await SetDateAsync(s, lot, new DateOnly(2024, 1, 1));

        (result.Error.Code, result.Error.Kind).ShouldBe(("portfolio.lot_from_trade", ErrorKind.Conflict));
        lot.AcquiredOn.ShouldBe(new DateOnly(2024, 3, 5));
    }

    [Fact]
    public async Task The_opening_lot_stays_editable_but_only_to_a_date_before_the_first_trade()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        var opening = s.Holding.Lots.Single(l => l.Source == LotSource.Import && l.IsOpen);

        var onTheFirstTrade = await SetDateAsync(s, opening, new DateOnly(2024, 3, 5));
        var afterIt = await SetDateAsync(s, opening, new DateOnly(2024, 7, 1));
        var before = Ok(await SetDateAsync(s, opening, new DateOnly(2023, 6, 1)));

        FieldsOf(onTheFirstTrade).ShouldBe(["acquiredOn"]);
        onTheFirstTrade.Error.Fields![0].Message.ShouldContain("2024-03-05");
        afterIt.Error.Code.ShouldBe("portfolio.invalid");
        (before.AcquiredOn, before.UsesDefaultDate).ShouldBe((new DateOnly(2023, 6, 1), false));
    }

    [Fact]
    public async Task A_lot_that_a_sync_added_after_the_rebuild_is_edited_like_any_other()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        s.Fx.Dhan.Rows[0] = Row("EXMPL", 35, 102m, 120m, "1001", Isin);
        Ok(await s.Fx.SyncAsync(s.Account.Id));
        var added = s.Holding.Lots.Single(l => l.Source == LotSource.Added);

        var result = Ok(await SetDateAsync(s, added, new DateOnly(2026, 9, 1)));

        result.AcquiredOn.ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task A_holding_without_trade_lots_keeps_editing_its_dates_as_before()
    {
        var s = await StartAsync();

        var result = Ok(await SetDateAsync(s, s.Holding.Lots.Single(), new DateOnly(2024, 3, 5)));

        result.AcquiredOn.ShouldBe(new DateOnly(2024, 3, 5));
    }

    [Fact]
    public async Task A_hand_edit_of_the_opening_lot_is_replaced_by_the_next_rebuild_and_counted_in_the_preview()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        Ok(await SetDateAsync(s, s.Holding.Lots.Single(l => l.Source == LotSource.Import && l.IsOpen), new DateOnly(2023, 6, 1)));

        var rebuild = Ok(await s.RebuildAsync(dryRun: true)).Rebuild;

        (rebuild.Status, rebuild.HandDatedLotsReplaced).ShouldBe(("ok", 1));
    }

    [Fact]
    public async Task A_changed_first_import_date_does_not_move_the_lots_of_a_holding_rebuilt_from_trades_but_moves_the_others()
    {
        var s = await StartAsync();
        s.Fx.Dhan.Rows.Add(Row("OTHER", 5, 20m, 25m, "2002", "INE000C01017"));
        Ok(await s.Fx.SyncAsync(s.Account.Id));
        await s.ImportOkAsync(Csv(BaseRows));
        var rebuilt = Shape(s.Holding);

        var saved = Ok(await s.Fx.Connections.Accounts.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(s.Account.Id, new PortfolioInput(null, null, FirstImportDate: new DateOnly(2024, 4, 1)), Actor), CancellationToken.None));

        Shape(s.Holding).ShouldBe(rebuilt);
        saved.Import.Redated.ShouldBe(0);
        saved.Import.RedateSkipped.ShouldBe(2);
    }

    [Fact]
    public async Task A_holding_rebuilt_from_a_sale_alone_is_also_left_alone_by_the_re_dating()
    {
        var s = await StartAsync(qty: 5);
        await s.ImportOkAsync(Csv("2024-03-05,EXMPL,INE000A01011,sell,2,130,T1"));

        Ok(await s.Fx.Connections.Accounts.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(s.Account.Id, new PortfolioInput(null, null, FirstImportDate: new DateOnly(2024, 4, 1)), Actor), CancellationToken.None));

        s.Holding.Lots.Single(l => l.IsOpen).AcquiredOn.ShouldBe(new DateOnly(2024, 3, 4));
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_and_a_real_run_applies_an_ok_rebuild()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows), rebuild: false);
        var lots = Shape(s.Holding);

        var dry = Ok(await s.RebuildAsync(dryRun: true));
        Shape(s.Holding).ShouldBe(lots);
        var real = Ok(await s.RebuildAsync(dryRun: false));

        (dry.DryRun, dry.Applied, dry.Rebuild.Status, dry.Rebuild.LotsAfter).ShouldBe((true, false, "ok", 4));
        (real.DryRun, real.Applied).ShouldBe((false, true));
        s.Holding.Lots.Count.ShouldBe(4);
        s.Fx.Events(AccountEventType.LotsRebuilt).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_holding_without_trades_is_rebuilt_into_the_one_default_dated_lot()
    {
        var s = await StartAsync();
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();

        var result = Ok(await s.RebuildAsync(dryRun: false));

        (result.Applied, result.Rebuild.HandDatedLotsReplaced).ShouldBe((true, 1));
        var lot = s.Holding.Lots.ShouldHaveSingleItem();
        (lot.AcquiredOn, lot.UsesDefaultDate, lot.Qty, lot.UnitCost, lot.Source).ShouldBe((new DateOnly(2025, 1, 1), true, 30L, 100m, LotSource.Import));
    }

    [Fact]
    public async Task A_rebuild_that_does_not_fit_is_not_applied()
    {
        var s = await StartAsync(qty: 10);
        await s.ImportOkAsync(Csv("2024-03-05,EXMPL,INE000A01011,buy,15,90,T1"));
        var lots = Shape(s.Holding);

        var result = Ok(await s.RebuildAsync(dryRun: false));

        (result.Applied, result.Rebuild.Status, result.Rebuild.Reason).ShouldBe((false, "quantity_mismatch", "negative_opening"));
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Events(AccountEventType.LotsRebuilt).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rebuild_is_refused_while_a_sync_runs_but_a_dry_run_is_not()
    {
        var s = await StartAsync();
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, Actor, s.Fx.Clock.GetUtcNow()));

        var real = await s.RebuildAsync(dryRun: false);
        var dry = await s.RebuildAsync(dryRun: true);

        real.Error.Code.ShouldBe("portfolio.sync_running");
        dry.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(BrokerSyncKind.Trades)]
    [InlineData(BrokerSyncKind.Ledger)]
    public async Task A_rebuild_is_refused_while_a_broker_history_run_of_the_account_runs_but_a_dry_run_and_another_accounts_run_are_not(BrokerSyncKind kind)
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(Guid.NewGuid(), kind, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));
        var elsewhere = await s.RebuildAsync(dryRun: false);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, kind, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var real = await s.RebuildAsync(dryRun: false);
        var dry = await s.RebuildAsync(dryRun: true);

        elsewhere.IsSuccess.ShouldBeTrue();
        real.Error.Code.ShouldBe("portfolio.sync_running");
        dry.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_holding_is_not_found_and_a_closed_holding_has_nothing_to_rebuild()
    {
        var s = await StartAsync();
        var unknown = await s.RebuildAsync(dryRun: true, Guid.NewGuid());
        s.Fx.Dhan.Rows.Clear();
        s.Fx.Dhan.Rows.Add(Row("OTHER", 5, 20m, 25m, "2002", "INE000C01017"));
        Ok(await s.Fx.SyncAsync(s.Account.Id));

        var closed = Ok(await s.RebuildAsync(dryRun: false));

        unknown.Error.Code.ShouldBe("portfolio.not_found");
        (s.Holding.Status, closed.Applied, closed.Rebuild.Status).ShouldBe((HoldingStatus.Closed, false, "no_holding"));
    }

    [Fact]
    public async Task Sales_alone_are_explained_by_a_larger_opening_lot()
    {
        var s = await StartAsync(qty: 5);

        var rebuild = (await s.PreviewOkAsync(Csv("2024-03-05,EXMPL,INE000A01011,sell,8,130,T1"))).Securities.Single().Rebuild;

        // 5 held and 8 sold: 13 shares were open before, which the opening lot stands for.
        (rebuild.Status, rebuild.OpeningQty.After).ShouldBe(("ok", 13L));
    }
}
