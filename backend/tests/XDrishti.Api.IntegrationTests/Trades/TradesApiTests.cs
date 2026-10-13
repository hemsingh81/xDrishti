using System.Net;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Api.IntegrationTests.Trades;

/// <summary>
/// Trade import and lot reconstruction over HTTP on the real database (P2-03, ADR 0023): preview, import with rebuild, the figures that follow from real dates and real sale prices, undo, re-import, mismatches, the guards and the caches.
/// Clock: Monday 2026-10-12 10:00 India time; EXMPL has 30 shares at 100; day bars Mon 5 to Fri 9 October.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TradesApiTests(ApiFactory factory) : TradesTestBase(factory)
{
    [Fact]
    public async Task The_preview_reports_the_file_and_the_rebuild_and_stores_nothing()
    {
        var held = await HeldAsync();
        var before = await LotsOfAsync(held.HoldingId);

        var report = await Client.PreviewTradesAsync(held.Account.Id, Base + "2026-10-07,NIFTY26OCTFUT,,buy,50,100,T9\n", Ct);

        report.File.ShouldSatisfyAllConditions(
            f => f.Rows.ShouldBe(4),
            f => f.Parsed.ShouldBe(3),
            f => f.Skipped.Total.ShouldBe(1),
            f => f.Skipped.ByReason["derivatives"].ShouldBe(1),
            f => f.BadTotal.ShouldBe(0),
            f => f.NewTrades.ShouldBe(3));
        var security = report.Securities.ShouldHaveSingleItem();
        (security.Symbol, security.Isin, security.Matched, security.HoldingId, security.Trades).ShouldBe(("EXMPL", TradesApi.Isin, true, held.HoldingId, 3));
        security.Rebuild.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe("ok"),
            r => r.LotsBefore.ShouldBe(1),
            r => r.LotsAfter.ShouldBe(4),
            r => r.Q.ShouldBe(30),
            r => r.Net.ShouldBe(12),
            r => r.OpeningQty.After.ShouldBe(18),
            r => r.Digest.Length.ShouldBe(64));
        report.Digest.Length.ShouldBe(64);
        (await TransactionsAsync()).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM portfolio.trade_batches")).ShouldBe(0);
        (await LotsOfAsync(held.HoldingId)).Lots.Select(l => (l.Id, l.AcquiredOn, l.Qty)).ShouldBe(before.Lots.Select(l => (l.Id, l.AcquiredOn, l.Qty)));
    }

    [Fact]
    public async Task Importing_with_a_rebuild_makes_the_lots_from_the_real_dates_and_sale_prices_and_the_figures_follow()
    {
        var held = await HeldAsync();
        var historyBefore = await HistoryAsync();
        var rowBefore = await RowAsync();

        var result = await Client.ImportTradesAsync(held.Account.Id, Base, Ct);

        (result.Imported, result.AlreadyImported, result.Rebuilt, result.HoldingsRebuilt, result.LotsReplaced).ShouldBe((3, 0, true, 1, 1));
        result.Batch.ShouldNotBeNull().ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe("imported"),
            b => b.FileName.ShouldBe("tradebook.csv"),
            b => b.AccountName.ShouldBe("Example One"),
            b => b.ActiveTrades.ShouldBe(3));
        var lots = await LotsOfAsync(held.HoldingId);
        lots.Lots.Select(l => (l.AcquiredOn, l.Qty, l.Status, l.Source, l.UsesDefaultDate, l.ClosedOn, l.ClosePrice, l.Estimated)).ShouldBe(
        [
            (new DateOnly(2026, 10, 4), 15L, "open", "import", true, null, null, false),
            (new DateOnly(2026, 10, 5), 10L, "open", "trade", false, null, null, false),
            (new DateOnly(2026, 10, 6), 5L, "open", "trade", false, null, null, false),
            (new DateOnly(2026, 10, 4), 3L, "closed", "import", true, new DateOnly(2026, 10, 8), 130m, false),
        ]);
        var row = await RowAsync();
        row.Qty.ShouldBe(30);
        row.AvgCost.ShouldBe(100m);
        row.Returns.Invested.ShouldBe(2991.667m, 0.01m); // 15 x 102.7778 + 1,450: first-in first-out against the broker's average cost of 3,000 (sales in the file)
        row.Returns.RealisedPnl.ShouldBe(3 * (130m - 102.7778m), 0.01m);
        row.Returns.RealisedEstimated.ShouldBeFalse();
        (row.LotCount, row.EarliestAcquiredOn).ShouldBe((3, new DateOnly(2026, 10, 4)));
        rowBefore.Returns.RealisedPnl.ShouldBe(0m);
        var historyAfter = await HistoryAsync();
        historyBefore.FirstDate.ShouldBe(Oct(9));
        historyAfter.FirstDate.ShouldBe(Oct(5));
        historyAfter.Points[0].Invested.ShouldBe((18 * 102.7778m) + 900m, 0.01m);
        historyAfter.Points[^1].Invested.ShouldBe(2991.667m, 0.01m);
    }

    [Fact]
    public async Task The_xirr_is_computed_again_from_the_new_lots()
    {
        var held = await HeldAsync();
        var before = (await RowAsync()).Returns;

        await Client.ImportTradesAsync(
            held.Account.Id,
            Rows("2025-03-05,EXMPL,INE000A01011,buy,10,90,T1", "2025-06-12,EXMPL,INE000A01011,buy,5,110,T2", "2025-09-20,EXMPL,INE000A01011,sell,3,130,T3"),
            Ct);
        var after = (await RowAsync()).Returns;

        // The default date of the import is three days old: no rate to speak of. With the real dates of a year ago there is one.
        (before.XirrState, before.Xirr).ShouldBe(("insufficient", null));
        after.XirrState.ShouldBe("ok");
        after.Xirr!.Value.ShouldBeGreaterThan(0m);
        after.RealisedPnl.ShouldBe(3 * (130m - 102.7778m), 0.01m);
    }

    [Fact]
    public async Task Undoing_the_import_brings_back_the_one_default_dated_lot_and_the_history()
    {
        var held = await HeldAsync();
        var batch = (await Client.ImportTradesAsync(held.Account.Id, Base, Ct)).Batch!;
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(5));

        var undone = await Client.UndoAsync(batch.Id, Ct);

        (undone.AlreadyUndone, undone.Voided, undone.HoldingsRebuilt, undone.Batch.Status, undone.Batch.ActiveTrades).ShouldBe((false, 3, 1, "undone", 0));
        var lot = (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem();
        (lot.AcquiredOn, lot.Qty, lot.UnitCost, lot.Source, lot.UsesDefaultDate, lot.Status).ShouldBe((Oct(9), 30L, 100m, "import", true, "open"));
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(9));
        (await TransactionsAsync()).ShouldBe(3);
        (await TransactionsAsync("voided_at IS NOT NULL AND voided_by IS NOT NULL")).ShouldBe(3);
        (await Client.UndoAsync(batch.Id, Ct)).AlreadyUndone.ShouldBeTrue();
    }

    [Fact]
    public async Task Importing_the_same_file_again_changes_nothing_and_after_an_undo_it_works_again()
    {
        var held = await HeldAsync();
        var first = await Client.ImportTradesAsync(held.Account.Id, Base, Ct);
        var lots = (await LotsOfAsync(held.HoldingId)).Lots.Select(l => l.Id).Order().ToList();

        var again = await Client.ImportTradesAsync(held.Account.Id, Base, Ct);

        (again.Imported, again.AlreadyImported, again.Batch, again.HoldingsRebuilt).ShouldBe((0, 3, null, 0));
        (await LotsOfAsync(held.HoldingId)).Lots.Select(l => l.Id).Order().ShouldBe(lots);
        (await TransactionsAsync()).ShouldBe(3);
        (await Client.BatchesAsync("", Ct)).Total.ShouldBe(1);

        await Client.UndoAsync(first.Batch!.Id, Ct);
        var third = await Client.ImportTradesAsync(held.Account.Id, Base, Ct);

        (third.Imported, third.AlreadyImported, third.HoldingsRebuilt).ShouldBe((3, 0, 1));
        (await TransactionsAsync("voided_at IS NULL")).ShouldBe(3);
        (await Client.BatchesAsync("", Ct)).Items.Select(b => b.Status).ShouldBe(["imported", "undone"]);
    }

    [Fact]
    public async Task A_holding_the_trades_do_not_fit_is_stored_reported_and_left_untouched()
    {
        var held = await HeldAsync(qty: 10);
        var before = await LotsOfAsync(held.HoldingId);

        var result = await Client.ImportTradesAsync(held.Account.Id, Rows("2026-10-05,EXMPL,INE000A01011,buy,15,90,T1"), Ct);

        result.Report.Securities.Single().Rebuild.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe("quantity_mismatch"),
            r => r.Reason.ShouldBe("negative_opening"),
            r => r.Q.ShouldBe(10),
            r => r.Net.ShouldBe(15));
        (result.Imported, result.HoldingsRebuilt).ShouldBe((1, 0));
        (await LotsOfAsync(held.HoldingId)).Lots.Select(l => l.Id).ShouldBe(before.Lots.Select(l => l.Id));
    }

    [Fact]
    public async Task Trades_of_securities_that_are_not_held_are_stored_and_listed_as_such()
    {
        var held = await HeldAsync();

        var result = await Client.ImportTradesAsync(held.Account.Id, Rows("2026-10-05,ACME,INE000B01019,buy,10,90,T1", "2026-10-06,ACME,INE000B01019,sell,10,95,T2"), Ct);

        (result.Imported, result.HoldingsRebuilt, result.Report.NotHeld.Securities, result.Report.NotHeld.Trades, result.Report.Unmatched.Securities).ShouldBe((2, 0, 1, 2, 1));
        (await TransactionsAsync()).ShouldBe(2);
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().Source.ShouldBe("import");
    }

    [Fact]
    public async Task While_the_accounts_sync_runs_the_import_the_undo_and_a_rebuild_are_refused()
    {
        var held = await HeldAsync();
        var batch = (await Client.ImportTradesOnlyAsync(held.Account.Id, Base, Ct)).Batch!;
        await ExecuteAsync(
            "INSERT INTO portfolio.holding_syncs (id, account_id, \"trigger\", requested_by, started_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count) VALUES (gen_random_uuid(), @a, 'manual', 'o', now(), 'running', 0, 0, 0, 0, 0)",
            ("a", held.Account.Id));

        using var import = await Client.ImportRawAsync(held.Account.Id, Rows("2026-10-07,EXMPL,INE000A01011,buy,1,100,T9"), rebuild: false, digest: null, Ct);
        using var undo = await Client.PostAsync($"/api/portfolio/trade-batches/{batch.Id}/undo", Ct);
        using var rebuild = await Client.PostAsync($"/api/holdings/{held.HoldingId}/rebuild-lots?dryRun=false", Ct);
        using var dry = await Client.PostAsync($"/api/holdings/{held.HoldingId}/rebuild-lots?dryRun=true", Ct);

        foreach (var response in new[] { import, undo, rebuild })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.sync_running");
        }

        dry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TransactionsAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task A_stale_preview_is_refused_with_the_fresh_report_and_nothing_is_applied()
    {
        var held = await HeldAsync();
        var digest = (await Client.PreviewTradesAsync(held.Account.Id, Base, Ct)).Digest;
        await SetLotDateAsync("EXMPL", "2026-10-02");

        using var response = await Client.ImportRawAsync(held.Account.Id, Base, rebuild: true, digest, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ProblemAsync(Ct);
        problem.GetProperty("code").GetString().ShouldBe("portfolio.preview_stale");
        var report = problem.GetProperty("report");
        report.GetProperty("digest").GetString().ShouldNotBe(digest);
        report.GetProperty("securities")[0].GetProperty("rebuild").GetProperty("handDatedLotsReplaced").GetInt32().ShouldBe(1);
        (await TransactionsAsync()).ShouldBe(0);
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().AcquiredOn.ShouldBe(new DateOnly(2026, 10, 2));
    }

    [Fact]
    public async Task A_rebuild_needs_the_digest_and_an_import_without_one_stores_only_the_trades()
    {
        var held = await HeldAsync();

        using var missing = await Client.ImportRawAsync(held.Account.Id, Base, rebuild: true, digest: null, Ct);
        var only = await Client.ImportTradesOnlyAsync(held.Account.Id, Base, Ct);

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.ReadFieldErrorsAsync(Ct)).ShouldContainKey("digest");
        (only.Imported, only.Rebuilt, only.HoldingsRebuilt).ShouldBe((3, false, 0));
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().Source.ShouldBe("import");
    }

    [Fact]
    public async Task A_sync_that_changes_the_holding_while_a_rebuild_runs_is_not_overwritten_and_the_rebuild_runs_again_on_what_it_left()
    {
        var held = await HeldAsync();
        await Client.ImportTradesOnlyAsync(held.Account.Id, Base, Ct);
        Factory.TradeHooks.Arm(() => ExecuteAsync("UPDATE portfolio.holdings SET qty = 31, last_price = 777 WHERE id = @id", ("id", held.HoldingId)));

        var result = await Client.RebuildLotsAsync(held.HoldingId, dryRun: false, Ct);

        Factory.TradeHooks.Armed.ShouldBeFalse();
        (result.Applied, result.Rebuild.Status, result.Rebuild.Q, result.Rebuild.OpeningQty.After).ShouldBe((true, "ok", 31L, 19L));
        var row = await RowAsync();
        (row.Qty, row.LastPrice, row.LotCount).ShouldBe((31L, 777m, 3));
        (await LotsOfAsync(held.HoldingId)).Lots.Where(l => l.Status == "open").Sum(l => l.Qty).ShouldBe(31);
    }

    [Fact]
    public async Task A_sync_that_changes_the_quantity_after_the_preview_makes_the_import_stale_even_when_it_happens_during_the_import()
    {
        var held = await HeldAsync();
        var digest = (await Client.PreviewTradesAsync(held.Account.Id, Base, Ct)).Digest;
        Factory.TradeHooks.Arm(() => ExecuteAsync("UPDATE portfolio.holdings SET qty = 31 WHERE id = @id", ("id", held.HoldingId)));

        using var response = await Client.ImportRawAsync(held.Account.Id, Base, rebuild: true, digest, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.preview_stale");
        (await TransactionsAsync()).ShouldBe(0);
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().Qty.ShouldBe(30);
    }

    [Fact]
    public async Task A_confirmed_split_between_the_trades_and_today_is_applied_and_the_preview_says_how_many()
    {
        var held = await HeldAsync(qty: 40, avgCost: 50m);
        await AddSplitAsync(held.Instrument, "2026-10-07", 1, 2);

        var report = await Client.PreviewTradesAsync(held.Account.Id, Rows("2026-10-05,EXMPL,INE000A01011,buy,10,100,T1"), Ct);

        report.Securities.Single().Rebuild.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe("ok"),
            r => r.AdjustedFor.ShouldBe(1),
            r => r.Net.ShouldBe(20),
            r => r.OpeningQty.After.ShouldBe(20));
        var result = await Client.ImportTradesAsync(held.Account.Id, Rows("2026-10-05,EXMPL,INE000A01011,buy,10,100,T1"), Ct);
        var bought = (await LotsOfAsync(held.HoldingId)).Lots.Single(l => l.Source == "trade");
        (bought.Qty, bought.UnitCost).ShouldBe((20L, 50m));
        result.HoldingsRebuilt.ShouldBe(1);
    }

    [Fact]
    public async Task The_preview_reads_the_holdings_the_trades_the_master_and_the_corporate_actions_once_each()
    {
        var held = await HeldAsync();
        await Client.ImportTradesOnlyAsync(held.Account.Id, Base, Ct);

        var statements = await Client.StatementsDuringAsync(async () => (await Client.PreviewTradesAsync(held.Account.Id, Base, Ct)).Securities.Count.ShouldBe(1));

        statements.Count(s => s.Contains("portfolio.holding_lots", StringComparison.Ordinal)).ShouldBe(1);
        statements.Count(s => s.Contains("FROM portfolio.transactions", StringComparison.Ordinal)).ShouldBe(1);
        statements.Count(s => s.Contains("market.corporate_actions", StringComparison.Ordinal)).ShouldBe(1);
        statements.Count(s => s.Contains("market.instruments", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task A_file_of_twenty_thousand_rows_is_previewed_within_the_bound()
    {
        var held = await HeldAsync();
        var rows = new List<string>(20_000);
        for (var i = 0; i < 20_000; i++)
        {
            var symbol = i % 40 == 0 ? "EXMPL" : $"SYM{i % 400:000}";
            rows.Add($"2026-10-0{(i % 5) + 1},{symbol},,{(i % 3 == 0 ? "sell" : "buy")},{(i % 7) + 1},{100 + (i % 50)},TID{i}");
        }

        // The work is counted, not only timed: one read each of the holdings, the stored trades, the master list and the corporate actions, whatever the number of rows.
        TradePreviewReport report = null!;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var statements = await Client.StatementsDuringAsync(async () =>
        {
            using var response = await Client.PreviewRawAsync(held.Account.Id, Rows([.. rows]), Ct);
            report = await response.ReadAsync<TradePreviewReport>(Ct);
        });
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

        report.File.Parsed.ShouldBe(20_000);
        (report.SecuritiesTotal, report.Securities.Count, report.UnmatchedListed).ShouldBe((391, 201, 200));
        (report.File.Rows, report.File.NewTrades).ShouldBe((20_000, 20_000));
        statements.Count(x => x.Contains("FROM portfolio.transactions", StringComparison.Ordinal)).ShouldBe(1);
        statements.Count(x => x.Contains("market.instruments", StringComparison.Ordinal)).ShouldBe(1);
        statements.Count(x => x.Contains("market.corporate_actions", StringComparison.Ordinal)).ShouldBe(1);
        statements.Count(x => x.Contains("portfolio.holding_lots", StringComparison.Ordinal)).ShouldBe(1);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10)); // generous: it takes well under a second
    }

    [Fact]
    public async Task The_history_cache_is_dropped_by_an_import_an_undo_and_a_rebuild()
    {
        var held = await HeldAsync();
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(9));
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(9));

        var batch = (await Client.ImportTradesAsync(held.Account.Id, Base, Ct)).Batch!;
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(5));

        await Client.UndoAsync(batch.Id, Ct);
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(9));

        await Client.ImportTradesOnlyAsync(held.Account.Id, Rows("2026-10-06,EXMPL,INE000A01011,buy,30,100,T9"), Ct);
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(9));
        (await Client.RebuildLotsAsync(held.HoldingId, dryRun: false, Ct)).Applied.ShouldBeTrue();
        (await HistoryAsync()).FirstDate.ShouldBe(Oct(6));
    }

    [Fact]
    public async Task The_dates_of_the_lots_from_trades_are_read_only_and_the_opening_lot_stays_before_the_first_trade()
    {
        var held = await HeldAsync();
        await Client.ImportTradesAsync(held.Account.Id, Base, Ct);
        var lots = (await LotsOfAsync(held.HoldingId)).Lots;
        var tradeLot = lots.First(l => l.Source == "trade");
        var opening = lots.First(l => l.Source == "import" && l.Status == "open");

        using var fromTrade = await Client.PutJsonAsync($"/api/holdings/{held.HoldingId}/lots/{tradeLot.Id}", new { acquiredOn = "2026-10-01" }, Ct);
        using var late = await Client.PutJsonAsync($"/api/holdings/{held.HoldingId}/lots/{opening.Id}", new { acquiredOn = "2026-10-05" }, Ct);
        using var early = await Client.PutJsonAsync($"/api/holdings/{held.HoldingId}/lots/{opening.Id}", new { acquiredOn = "2026-09-01" }, Ct);

        fromTrade.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await fromTrade.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.lot_from_trade");
        late.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await late.ReadFieldErrorsAsync(Ct)).ShouldContainKey("acquiredOn");
        early.StatusCode.ShouldBe(HttpStatusCode.OK, await early.Content.ReadAsStringAsync(Ct));
        var changed = (await LotsOfAsync(held.HoldingId)).Lots.Single(l => l.Id == opening.Id);
        (changed.AcquiredOn, changed.UsesDefaultDate).ShouldBe((new DateOnly(2026, 9, 1), false));
    }

    [Fact]
    public async Task A_changed_first_import_date_does_not_move_the_lots_of_a_holding_rebuilt_from_trades()
    {
        var held = await HeldAsync();
        await Client.ImportTradesAsync(held.Account.Id, Base, Ct);
        var before = (await LotsOfAsync(held.HoldingId)).Lots.Select(l => (l.Id, l.AcquiredOn)).Order().ToList();

        var saved = await Client.SavePortfolioAsync(held.Account.Id, new { firstImportDate = "2026-10-01" }, Ct);

        (saved.Import!.Redated, saved.Import.RedateSkipped).ShouldBe((0, 2));
        (await LotsOfAsync(held.HoldingId)).Lots.Select(l => (l.Id, l.AcquiredOn)).Order().ShouldBe(before);
    }

    [Fact]
    public async Task Manual_trades_are_added_listed_voided_and_counted_in_the_next_rebuild()
    {
        var held = await HeldAsync();

        using var added = await Client.PostJsonAsync("/api/portfolio/trades", new { holdingId = held.HoldingId, side = "buy", tradeDate = "2026-10-05", quantity = 10, price = 90.5, charges = 12.5 }, Ct);
        var trade = await added.ReadAsync<TradeView>(Ct);
        using var second = await Client.PostJsonAsync("/api/portfolio/trades", new { holdingId = held.HoldingId, side = "buy", tradeDate = "2026-10-05", quantity = 10, price = 90.5 }, Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await Client.HoldingTradesAsync(held.HoldingId, "", Ct);
        using var voided = await Client.PostAsync($"/api/portfolio/trades/{trade.Id}/void", Ct);
        var voidedTrade = await voided.ReadAsync<TradeView>(Ct);
        var dry = await Client.RebuildLotsAsync(held.HoldingId, dryRun: true, Ct);
        var after = await Client.HoldingTradesAsync(held.HoldingId, "?pageSize=1&page=2", Ct);

        (trade.Source, trade.Side, trade.Qty, trade.Price, trade.Charges, trade.Voided, trade.BatchId).ShouldBe(("manual", "buy", 10L, 90.5m, 12.5m, false, null));
        page.Total.ShouldBe(2);
        (voidedTrade.Voided, voidedTrade.VoidedBy).ShouldBe((true, ApiFactory.OwnerEmail));
        (dry.DryRun, dry.Applied, dry.Rebuild.Status, dry.Rebuild.Net).ShouldBe((true, false, "ok", 10L));
        (after.Total, after.Items.Count).ShouldBe((2, 1));
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem().Source.ShouldBe("import");
        (await TransactionsAsync("source = 'manual'")).ShouldBe(2);
    }

    [Fact]
    public async Task A_manual_trade_with_bad_values_is_a_400_with_the_fields()
    {
        var held = await HeldAsync();

        using var response = await Client.PostJsonAsync("/api/portfolio/trades", new { holdingId = held.HoldingId, side = "hold", tradeDate = "2027-01-01", quantity = 1.5, price = 0 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["side", "tradeDate", "quantity", "price"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_import_history_lists_the_batches_newest_first_and_the_template_downloads_as_csv()
    {
        var held = await HeldAsync();
        await Client.ImportTradesOnlyAsync(held.Account.Id, Rows(TradesApi.BaseRows[0]), Ct);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await Client.ImportTradesOnlyAsync(held.Account.Id, Rows(TradesApi.BaseRows[1]), Ct);

        var batches = await Client.BatchesAsync($"?accountId={held.Account.Id}&pageSize=1", Ct);
        using var template = await Client.GetAsync("/api/portfolio/trades/template", Ct);

        (batches.Total, batches.Items.Count, batches.Items[0].Imported).ShouldBe((2, 1, 1));
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        template.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        template.Content.Headers.ContentDisposition!.FileName.ShouldBe(TradeTemplate.FileName);
        (await template.Content.ReadAsStringAsync(Ct)).ShouldBe(TradeTemplate.Csv);
    }

    [Fact]
    public async Task The_template_is_a_file_the_import_accepts()
    {
        var held = await HeldAsync();

        var report = await Client.PreviewTradesAsync(held.Account.Id, TradeTemplate.Csv, Ct);

        (report.File.Parsed, report.File.BadTotal).ShouldBe((2, 0));
        report.Securities.Single().Symbol.ShouldBe("EXMPL");
    }

    [Fact]
    public async Task Bad_uploads_are_400_and_an_unknown_account_is_404_and_no_portfolio_role_is_409()
    {
        var held = await HeldAsync();
        var plain = await Client.CreateAccountAsync(AccountsApi.NewAccount("Plain", "1100000002"), Ct);

        using var noFile = await Client.PostAsync(new Uri("/api/portfolio/trades/preview", UriKind.Relative), new MultipartFormDataContent { { new StringContent(held.Account.Id.ToString()), "accountId" } }, Ct);
        using var notMultipart = await Client.PostJsonAsync("/api/portfolio/trades/preview", new { accountId = held.Account.Id }, Ct);
        using var headerOnly = await Client.PreviewRawAsync(held.Account.Id, "when,what\n1,2\n", Ct);
        using var unknown = await Client.PreviewRawAsync(Guid.NewGuid(), Base, Ct);
        using var noRole = await Client.PreviewRawAsync(plain.Id, Base, Ct);
        using var notUtf8 = await Client.PostAsync(new Uri("/api/portfolio/trades/preview", UriKind.Relative), new MultipartFormDataContent { { new StringContent(held.Account.Id.ToString()), "accountId" }, { new ByteArrayContent([0xE9, 0xFF, 0x2C, 0x0A]), "file", "t.csv" } }, Ct);

        notMultipart.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        foreach (var response in new[] { noFile, headerOnly, notUtf8 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadFieldErrorsAsync(Ct)).ShouldContainKey("file");
        }

        (await headerOnly.ReadFieldErrorsAsync(Ct))["file"][0].ShouldContain("Accepted header names");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.not_found");
        noRole.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await noRole.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.role_off");
    }

    [Fact]
    public async Task A_file_over_ten_megabytes_is_a_400_and_stores_nothing()
    {
        var held = await HeldAsync();
        var big = Rows(Enumerable.Repeat("2026-10-05,EXMPL,INE000A01011,buy,1,100," + new string('x', 60), 200_000).ToArray());

        using var response = await Client.ImportRawAsync(held.Account.Id, big, rebuild: false, digest: null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransactionsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_import_is_audited_with_counts_only_and_no_file_content_reaches_the_logs_or_the_audit_trail()
    {
        var held = await HeldAsync();

        await Client.ImportTradesAsync(held.Account.Id, Base, Ct);

        using var activity = await Client.GetAsync($"/api/accounts/{held.Account.Id}/activity", Ct);
        var text = await activity.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("Trades imported: 3 valid rows, 3 stored");
        text.ShouldContain("Lots rebuilt from trades: 1 holding");
        text.ShouldNotContain("EXMPL", Case.Sensitive);
        text.ShouldNotContain("tradebook.csv");
        AssertNoSecret(text);
        Factory.Logs.All.ShouldNotContain(l => l.Contains("tradebook.csv", StringComparison.Ordinal) || l.Contains(TradesApi.Isin, StringComparison.Ordinal));
        Factory.Logs.All.ShouldContain(l => l.Contains("Trades imported for account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_same_trade_id_on_two_days_is_two_trades_in_one_file_and_across_imports()
    {
        var held = await HeldAsync();

        var first = await Client.ImportTradesOnlyAsync(held.Account.Id, Rows("2026-10-05,EXMPL,INE000A01011,buy,10,90,1001", "2026-10-06,EXMPL,INE000A01011,buy,5,110,1001", "2026-10-06,EXMPL,INE000A01011,buy,5,110,1001"), Ct);
        var again = await Client.ImportTradesOnlyAsync(held.Account.Id, Rows("2026-10-05,EXMPL,INE000A01011,buy,10,90,1001", "2026-10-07,EXMPL,INE000A01011,buy,1,100,1001"), Ct);

        (first.Imported, first.Report.File.Duplicates).ShouldBe((2, 1));
        (again.Imported, again.AlreadyImported).ShouldBe((1, 1));
        (await TransactionsAsync("external_id LIKE 't:2026100_:1001'")).ShouldBe(3);
    }

    [Fact]
    public async Task A_dry_run_of_an_undo_over_http_changes_nothing_and_lists_what_cannot_come_back()
    {
        var held = await HeldAsync();
        var batch = (await Client.ImportTradesAsync(held.Account.Id, Base, Ct)).Batch!;
        await SetLotDateAsync("EXMPL", "2026-09-01");
        var lots = (await LotsOfAsync(held.HoldingId)).Lots.Select(l => l.Id).Order().ToList();

        using var response = await Client.PostAsync($"/api/portfolio/trade-batches/{batch.Id}/undo?dryRun=true", Ct);
        var dry = await response.ReadAsync<TradeUndoResult>(Ct);

        (dry.DryRun, dry.Applied, dry.Voided, dry.HoldingsRebuilt, dry.HandDatedLotsReplaced, dry.SyncLotsReplaced, dry.NotRebuilt.Count).ShouldBe((true, false, 3, 1, 1, 0, 0));
        dry.Batch.Status.ShouldBe("imported");
        (await LotsOfAsync(held.HoldingId)).Lots.Select(l => l.Id).Order().ShouldBe(lots);
        (await TransactionsAsync("voided_at IS NOT NULL")).ShouldBe(0);
        var real = await Client.UndoAsync(batch.Id, Ct);
        (real.DryRun, real.Applied, real.HandDatedLotsReplaced).ShouldBe((false, true, 1));
    }

    [Fact]
    public async Task Rebuild_and_undo_read_one_securitys_trades_not_the_whole_history()
    {
        var held = await HeldAsync();
        var batch = (await Client.ImportTradesAsync(held.Account.Id, Base, Ct)).Batch!;

        var rebuild = await Client.StatementsDuringAsync(async () => (await Client.RebuildLotsAsync(held.HoldingId, dryRun: true, Ct)).Rebuild.Status.ShouldBe("unchanged"));
        using var undoDry = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/portfolio/trade-batches/{batch.Id}/undo?dryRun=true", UriKind.Relative));
        var undo = await Client.StatementsDuringAsync(async () => (await Client.SendAsync(undoDry, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK));

        foreach (var statements in new[] { rebuild, undo })
        {
            var reads = statements.Where(x => x.Contains("FROM portfolio.transactions", StringComparison.Ordinal)).ToList();
            reads.ShouldNotBeEmpty();
            reads.ShouldAllBe(x => x.Contains("trading_symbol", StringComparison.Ordinal) || x.Contains("batch_id", StringComparison.Ordinal) || x.Contains(".isin", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Every_change_of_trades_drops_the_cached_histories_an_import_an_add_a_void_an_undo_and_a_rebuild()
    {
        var held = await HeldAsync();
        var cache = Factory.Services.GetRequiredService<IPortfolioHistoryCache>();
        long Version() => cache.Version;

        var before = Version();
        var batch = (await Client.ImportTradesAsync(held.Account.Id, Base, Ct)).Batch!;
        var afterImport = Version();
        using var added = await Client.PostJsonAsync("/api/portfolio/trades", new { holdingId = held.HoldingId, side = "buy", tradeDate = "2026-10-05", quantity = 1, price = 90 }, Ct);
        var trade = await added.ReadAsync<TradeView>(Ct);
        var afterAdd = Version();
        using var voided = await Client.PostAsync($"/api/portfolio/trades/{trade.Id}/void", Ct);
        voided.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterVoid = Version();
        var rebuilt = await Client.RebuildLotsAsync(held.HoldingId, dryRun: false, Ct);
        var afterRebuild = Version();
        await Client.UndoAsync(batch.Id, Ct);
        var afterUndo = Version();

        afterImport.ShouldBeGreaterThan(before);
        afterAdd.ShouldBeGreaterThan(afterImport);
        afterVoid.ShouldBeGreaterThan(afterAdd);
        rebuilt.Rebuild.Status.ShouldBe("unchanged"); // the voided trade is gone again: nothing to rebuild, and the unit of work drops the caches all the same
        afterRebuild.ShouldBeGreaterThan(afterVoid);
        afterUndo.ShouldBeGreaterThan(afterRebuild);
    }
}
