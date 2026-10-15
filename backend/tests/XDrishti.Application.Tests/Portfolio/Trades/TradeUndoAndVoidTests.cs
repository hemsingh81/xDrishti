using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeScenario;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>Undoing an import and voiding a trade (ADR 0023; P2-03 criteria 14, 15, 16a): nothing is deleted, the lots are derived again from what remains.</summary>
public sealed class TradeUndoAndVoidTests
{
    private static readonly DateOnly FirstImport = new(2025, 1, 1);

    private static readonly (DateOnly, long, decimal, LotSource, bool, DateOnly?, decimal?, bool) DefaultLot = (FirstImport, 30L, 100m, LotSource.Import, true, null, null, false);

    [Fact]
    public async Task Undoing_an_import_voids_its_trades_and_brings_the_holding_back_to_one_default_dated_lot()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;

        var undone = Ok(await s.UndoAsync(batch.Id));

        (undone.AlreadyUndone, undone.Voided, undone.HoldingsRebuilt, undone.HoldingsNotRebuilt).ShouldBe((false, 3, 1, 0));
        (undone.Batch.Status, undone.Batch.ActiveTrades, undone.Batch.UndoneAt).ShouldBe(("undone", 0, s.Fx.Clock.GetUtcNow()));
        s.Data.Trades.ShouldAllBe(t => t.IsVoided);
        s.Data.Trades.Count.ShouldBe(3);
        Shape(s.Holding).ShouldBe([DefaultLot]);
    }

    [Fact]
    public async Task Undoing_twice_is_a_no_op()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;
        Ok(await s.UndoAsync(batch.Id));
        var events = s.Fx.Accounts.Events.Count;
        var saves = s.Fx.Data.SaveCount;

        var again = Ok(await s.UndoAsync(batch.Id));

        (again.AlreadyUndone, again.Voided, again.HoldingsRebuilt).ShouldBe((true, 0, 0));
        s.Fx.Accounts.Events.Count.ShouldBe(events);
        s.Fx.Data.SaveCount.ShouldBe(saves);
        Shape(s.Holding).ShouldBe([DefaultLot]);
    }

    [Fact]
    public async Task The_same_file_can_be_imported_again_after_an_undo()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;
        Ok(await s.UndoAsync(batch.Id));

        var again = await s.ImportOkAsync(Csv(BaseRows));

        (again.Imported, again.AlreadyImported, again.HoldingsRebuilt).ShouldBe((3, 0, 1));
        s.Data.Trades.Count(t => !t.IsVoided).ShouldBe(3);
        s.Data.Batches.Count.ShouldBe(2);
        s.OpenQty().ShouldBe(30);
    }

    [Fact]
    public async Task Undoing_one_of_two_imports_rebuilds_from_the_other()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows[0], BaseRows[1]));
        var second = (await s.ImportOkAsync(Csv(BaseRows[2]))).Batch!;

        Ok(await s.UndoAsync(second.Id));

        s.Data.Trades.Count(t => !t.IsVoided).ShouldBe(2);
        s.Holding.Lots.ShouldAllBe(l => l.ClosedOn == null);
        s.OpenQty().ShouldBe(30);
        s.Holding.Lots.Single(l => l.Source == LotSource.Import).Qty.ShouldBe(15);
    }

    [Fact]
    public async Task Undoing_an_import_that_never_rebuilt_leaves_the_owners_lot_dates_alone()
    {
        var s = await StartAsync();
        var lot = s.Holding.Lots.Single();
        s.Holding.ChangeLotDate(lot.Id, new DateOnly(2023, 2, 2), s.Fx.Today).IsSuccess.ShouldBeTrue();
        var batch = (await s.ImportOkAsync(Csv(BaseRows), rebuild: false)).Batch!;

        var undone = Ok(await s.UndoAsync(batch.Id));

        (undone.Voided, undone.HoldingsRebuilt).ShouldBe((3, 0));
        var kept = s.Holding.Lots.ShouldHaveSingleItem();
        (kept.AcquiredOn, kept.UsesDefaultDate).ShouldBe((new DateOnly(2023, 2, 2), false));
    }

    [Fact]
    public async Task Undoing_is_audited_with_counts_only()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;

        Ok(await s.UndoAsync(batch.Id));

        s.Fx.Events(AccountEventType.TradesUndone).ShouldHaveSingleItem().Detail.ShouldBe("Trade import undone: 3 trades voided, 1 holdings rebuilt, 0 not rebuilt");
        s.Fx.Events(AccountEventType.LotsRebuilt).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Undoing_while_a_sync_runs_and_undoing_an_unknown_import_are_refused()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, Actor, s.Fx.Clock.GetUtcNow()));

        var running = await s.UndoAsync(batch.Id);
        var unknown = await s.UndoAsync(Guid.NewGuid());

        running.Error.Code.ShouldBe("portfolio.sync_running");
        unknown.Error.Code.ShouldBe("portfolio.not_found");
        s.Data.Trades.ShouldAllBe(t => !t.IsVoided);
    }

    [Theory]
    [InlineData(BrokerSyncKind.Trades)]
    [InlineData(BrokerSyncKind.Ledger)]
    public async Task Undoing_while_a_broker_history_run_of_the_account_runs_is_refused(BrokerSyncKind kind)
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, kind, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var running = await s.UndoAsync(batch.Id);

        running.Error.Code.ShouldBe("portfolio.sync_running");
        s.Data.Trades.ShouldAllBe(t => !t.IsVoided);
    }

    [Fact]
    public async Task A_broker_history_run_of_another_account_does_not_hold_the_undo_back()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Csv(BaseRows))).Batch!;
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(Guid.NewGuid(), BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var undone = await s.UndoAsync(batch.Id);

        undone.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Voiding_a_trade_stops_it_counting_and_frees_its_identity_for_a_later_import()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows), rebuild: false);
        var trade = s.Data.Trades.Single(t => t.ExternalId == "t:20240920:T3");

        var voided = Ok(await s.Fx.VoidTrade().HandleAsync(new VoidTradeCommand(trade.Id, Actor), CancellationToken.None));

        (voided.Voided, voided.VoidedBy).ShouldBe((true, Actor));
        var rebuild = Ok(await s.RebuildAsync(dryRun: true)).Rebuild;
        (rebuild.Net, rebuild.OpeningQty.After).ShouldBe((15L, 15L));
        var again = await s.ImportOkAsync(Csv(BaseRows), rebuild: false);
        (again.Imported, again.AlreadyImported).ShouldBe((1, 2));
    }

    [Fact]
    public async Task Voiding_twice_is_a_no_op_and_an_unknown_trade_is_not_found()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows), rebuild: false);
        var id = s.Data.Trades[0].Id;
        Ok(await s.Fx.VoidTrade().HandleAsync(new VoidTradeCommand(id, Actor), CancellationToken.None));
        var events = s.Fx.Events(AccountEventType.TradeVoided).Count;

        var again = Ok(await s.Fx.VoidTrade().HandleAsync(new VoidTradeCommand(id, "someone else"), CancellationToken.None));
        var unknown = await s.Fx.VoidTrade().HandleAsync(new VoidTradeCommand(Guid.NewGuid(), Actor), CancellationToken.None);

        events.ShouldBe(1);
        s.Fx.Events(AccountEventType.TradeVoided).Count.ShouldBe(1);
        again.VoidedBy.ShouldBe(Actor);
        unknown.Error.Code.ShouldBe("portfolio.not_found");
    }

    [Fact]
    public async Task Voiding_changes_no_lot_until_the_owner_rebuilds()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        var lots = Shape(s.Holding);

        Ok(await s.Fx.VoidTrade().HandleAsync(new VoidTradeCommand(s.Data.Trades.Single(t => t.ExternalId == "t:20240920:T3").Id, Actor), CancellationToken.None));

        Shape(s.Holding).ShouldBe(lots);
        Ok(await s.RebuildAsync(dryRun: true)).Rebuild.Status.ShouldBe("ok");
    }
}
