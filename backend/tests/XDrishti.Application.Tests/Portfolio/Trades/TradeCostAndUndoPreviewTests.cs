using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeScenario;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>The cost figures of a rebuild, the dry run and the limits of an undo, the identity of a trade id, the cap on the listed securities and the reads of a rebuild (review of P2-03).</summary>
public sealed class TradeCostAndUndoPreviewTests
{
    private static string Rows(params string[] rows) => Csv(rows);

    [Fact]
    public async Task A_sale_does_not_change_the_cost_of_the_opening_shares_and_the_difference_to_the_brokers_cost_is_shown()
    {
        var s = await StartAsync(qty: 100, avgCost: 65m);

        var report = await s.PreviewOkAsync(Rows("2024-03-05,EXMPL,INE000A01011,buy,150,80,T1", "2024-04-01,EXMPL,INE000A01011,sell,100,90,T2"));

        var rebuild = report.Securities.Single().Rebuild;
        (rebuild.OpenCostAfter, rebuild.BrokerCost, rebuild.CostDelta, rebuild.RealisedPnl, rebuild.CostNote).ShouldBe((8000m, 6500m, 1500m, 4000m, null));
        (rebuild.OpeningQty.After, rebuild.Net).ShouldBe((50L, 50L));
    }

    [Fact]
    public async Task Without_an_opening_lot_the_difference_is_shown_too()
    {
        var s = await StartAsync(qty: 15, avgCost: 99m);

        var rebuild = (await s.PreviewOkAsync(Rows(BaseRows[0], BaseRows[1]))).Securities.Single().Rebuild;

        (rebuild.OpenCostAfter, rebuild.BrokerCost, rebuild.CostDelta, rebuild.OpeningQty.After).ShouldBe((1450m, 1485m, -35m, 0L));
    }

    [Fact]
    public async Task When_the_trades_cost_more_than_the_average_allows_the_note_says_the_opening_lot_costs_the_average()
    {
        var s = await StartAsync(qty: 20, avgCost: 10m);

        var rebuild = (await s.PreviewOkAsync(Rows("2024-03-05,EXMPL,INE000A01011,buy,5,100,T1"))).Securities.Single().Rebuild;

        rebuild.CostNote.ShouldBe("opening_cost_fallback");
        s.Holding.Lots.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_status_that_does_not_fit_has_no_cost_figures()
    {
        var s = await StartAsync(qty: 10);

        var rebuild = (await s.PreviewOkAsync(Rows("2024-03-05,EXMPL,INE000A01011,buy,15,90,T1"))).Securities.Single().Rebuild;

        (rebuild.OpenCostAfter, rebuild.BrokerCost, rebuild.CostDelta, rebuild.CostNote).ShouldBe((null, null, null, null));
    }

    [Fact]
    public async Task The_same_trade_id_on_two_days_is_two_trades_across_imports_too_and_on_one_day_a_repeat()
    {
        var s = await StartAsync();

        await s.ImportOkAsync(Rows("2024-03-05,EXMPL,INE000A01011,buy,10,90,1001"), rebuild: false);
        var second = await s.ImportOkAsync(Rows("2024-03-06,EXMPL,INE000A01011,buy,5,110,1001"), rebuild: false);
        var again = await s.ImportOkAsync(Rows("2024-03-05,EXMPL,INE000A01011,buy,10,90,1001", "2024-03-06,EXMPL,INE000A01011,buy,5,110,1001"), rebuild: false);

        (second.Imported, second.AlreadyImported).ShouldBe((1, 0));
        (again.Imported, again.AlreadyImported).ShouldBe((0, 2));
        s.Data.Trades.Select(t => t.ExternalId).ShouldBe(["t:20240305:1001", "t:20240306:1001"]);
    }

    [Fact]
    public async Task A_dry_run_of_an_undo_changes_nothing_and_says_what_an_undo_would_do()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Rows(BaseRows))).Batch!;
        var lots = Shape(s.Holding);
        var events = s.Fx.Accounts.Events.Count;

        var dry = Ok(await s.Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batch.Id, Actor, DryRun: true), CancellationToken.None));

        (dry.DryRun, dry.Applied, dry.AlreadyUndone, dry.Voided, dry.HoldingsRebuilt, dry.LotsReplaced, dry.HoldingsNotRebuilt).ShouldBe((true, false, false, 3, 1, 4, 0));
        (dry.HandDatedLotsReplaced, dry.SyncLotsReplaced, dry.Batch.Status).ShouldBe((0, 0, "imported"));
        dry.NotRebuilt.ShouldBeEmpty();
        Shape(s.Holding).ShouldBe(lots);
        s.Data.Trades.ShouldAllBe(t => !t.IsVoided);
        s.Data.Batches.Single().Status.ShouldBe(TradeBatchStatus.Imported);
        s.Fx.Accounts.Events.Count.ShouldBe(events);

        var real = Ok(await s.UndoAsync(batch.Id));
        (real.DryRun, real.Applied, real.Voided, real.HoldingsRebuilt, real.LotsReplaced).ShouldBe((false, true, dry.Voided, dry.HoldingsRebuilt, dry.LotsReplaced));
    }

    [Fact]
    public async Task A_dry_run_of_an_undo_works_while_a_sync_runs_and_an_undone_batch_stays_a_no_op()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Rows(BaseRows))).Batch!;
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, Actor, s.Fx.Clock.GetUtcNow()));

        var dry = Ok(await s.Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batch.Id, Actor, DryRun: true), CancellationToken.None));
        s.Fx.Data.Syncs.Clear();
        Ok(await s.UndoAsync(batch.Id));
        var undoneDry = Ok(await s.Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batch.Id, Actor, DryRun: true), CancellationToken.None));

        dry.Applied.ShouldBeFalse();
        (undoneDry.AlreadyUndone, undoneDry.Applied, undoneDry.Voided).ShouldBe((true, false, 0));
    }

    [Fact]
    public async Task An_undo_counts_the_hand_dated_and_sync_added_lots_it_cannot_bring_back()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Rows(BaseRows))).Batch!;
        var opening = s.Holding.Lots.First(l => l.Source == LotSource.Import && l.IsOpen);
        s.Holding.ChangeLotDate(opening.Id, new DateOnly(2023, 6, 1), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Fx.Dhan.Rows[0] = Row("EXMPL", 35, 102m, 120m, "1001", Isin);
        Ok(await s.Fx.SyncAsync(s.Account.Id));

        var dry = Ok(await s.Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batch.Id, Actor, DryRun: true), CancellationToken.None));
        var real = Ok(await s.UndoAsync(batch.Id));

        (dry.HandDatedLotsReplaced, dry.SyncLotsReplaced).ShouldBe((1, 1));
        (real.HandDatedLotsReplaced, real.SyncLotsReplaced).ShouldBe((1, 1));
        s.Holding.Lots.ShouldHaveSingleItem().Qty.ShouldBe(35);
    }

    [Fact]
    public async Task An_undo_that_leaves_trades_that_do_not_fit_lists_the_holding_and_keeps_its_lots()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Rows(BaseRows[0], BaseRows[1]));
        var second = (await s.ImportOkAsync(Rows(BaseRows[2]))).Batch!;
        s.Fx.Dhan.Rows[0] = Row("EXMPL", 10, 100m, 120m, "1001", Isin);
        Ok(await s.Fx.SyncAsync(s.Account.Id));
        var lots = Shape(s.Holding);

        var dry = Ok(await s.Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(second.Id, Actor, DryRun: true), CancellationToken.None));
        var real = Ok(await s.UndoAsync(second.Id));

        foreach (var result in new[] { dry, real })
        {
            result.HoldingsNotRebuilt.ShouldBe(1);
            var entry = result.NotRebuilt.ShouldHaveSingleItem();
            (entry.Symbol, entry.HoldingId, entry.Reason).ShouldBe(("EXMPL", s.Holding.Id, "negative_opening"));
        }

        Shape(s.Holding).ShouldBe(lots);
        s.Data.Trades.Count(t => t.IsVoided).ShouldBe(1);
    }

    [Fact]
    public async Task Undo_and_rebuild_read_the_trades_of_the_holdings_security_not_the_whole_history()
    {
        var s = await StartAsync();
        var batch = (await s.ImportOkAsync(Rows(BaseRows))).Batch!;
        var reads = s.Data.WholeHistoryReads;

        Ok(await s.RebuildAsync(dryRun: true));
        Ok(await s.RebuildAsync(dryRun: false));
        Ok(await s.Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batch.Id, Actor, DryRun: true), CancellationToken.None));
        Ok(await s.UndoAsync(batch.Id));

        s.Data.WholeHistoryReads.ShouldBe(reads);
    }

    [Fact]
    public async Task A_report_lists_every_held_or_listed_security_and_at_most_200_unmatched_ones_and_counts_them_all()
    {
        var s = await StartAsync();
        var rows = new List<string>(BaseRows);
        rows.AddRange(Enumerable.Range(0, 250).Select(i => $"2024-04-02,SYM{i:000},,buy,1,10,U{i}"));
        s.Data.Instruments.Add(new XDrishti.Application.Abstractions.Portfolio.TradeInstrument(Guid.NewGuid(), "SYM005", null, "share"));

        var report = await s.PreviewOkAsync(Csv([.. rows]));
        var fewer = await s.PreviewOkAsync(Csv([.. BaseRows, .. rows.Skip(3).Take(10)]));

        report.SecuritiesTotal.ShouldBe(251);
        (report.Securities.Count, report.UnmatchedListed).ShouldBe((202, 200));
        report.Securities.Select(x => x.Symbol).ShouldContain("EXMPL");
        report.Securities.Select(x => x.Symbol).ShouldContain("SYM005");
        (report.NotHeld.Securities, report.Unmatched.Securities).ShouldBe((250, 249));
        (fewer.SecuritiesTotal, fewer.Securities.Count, fewer.UnmatchedListed).ShouldBe((11, 11, 9));
        report.Digest.ShouldNotBe(fewer.Digest);
    }

    [Fact]
    public async Task The_import_with_a_capped_report_still_checks_the_digest_of_everything()
    {
        var s = await StartAsync();
        var rows = new List<string>(BaseRows);
        rows.AddRange(Enumerable.Range(0, 250).Select(i => $"2024-04-02,SYM{i:000},,buy,1,10,U{i}"));

        var result = await s.ImportOkAsync(Csv([.. rows]));

        (result.Imported, result.Report.SecuritiesTotal, result.Report.Securities.Count).ShouldBe((253, 251, 201));
        s.Fx.Events(AccountEventType.TradesImported).ShouldHaveSingleItem();
    }
}
