using XDrishti.Application.Portfolio.Trades;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeScenario;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>Importing a tradebook and rebuilding the lots (ADR 0023; P2-03 criteria 11, 13, 13a, 16, 16a, 17) through the real handlers on in-memory ports.</summary>
public sealed class TradeImportTests
{
    [Fact]
    public async Task Importing_with_a_rebuild_stores_the_trades_and_replaces_the_lots_with_real_dates_and_real_sale_prices()
    {
        var s = await StartAsync();

        var result = await s.ImportOkAsync(Csv(BaseRows));

        (result.Imported, result.AlreadyImported, result.Rebuilt, result.HoldingsRebuilt, result.LotsReplaced).ShouldBe((3, 0, true, 1, 1));
        result.Batch!.ShouldSatisfyAllConditions(
            b => b.Parsed.ShouldBe(3),
            b => b.Imported.ShouldBe(3),
            b => b.Status.ShouldBe("imported"),
            b => b.FileName.ShouldBe("tradebook.csv"),
            b => b.Sha256.Length.ShouldBe(64),
            b => b.ActiveTrades.ShouldBe(3));
        s.Data.Trades.Count.ShouldBe(3);
        Shape(s.Holding).ShouldBe(
        [
            (D("2024-03-04"), 15L, 102.7778m, LotSource.Import, true, null, null, false),
            (D("2024-03-04"), 3L, 102.7778m, LotSource.Import, true, D("2024-09-20"), 130m, false),
            (D("2024-03-05"), 10L, 90m, LotSource.Trade, false, null, null, false),
            (D("2024-06-12"), 5L, 110m, LotSource.Trade, false, null, null, false),
        ]);
    }

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task After_a_rebuild_the_open_quantity_is_the_brokers_and_the_cost_difference_is_shown_when_the_file_has_sales()
    {
        var s = await StartAsync();

        var result = await s.ImportOkAsync(Csv(BaseRows));

        s.OpenQty().ShouldBe(30);
        s.Holding.Qty.ShouldBe(30);
        s.Holding.AvgCost.ShouldBe(100m);
        // c0 = (100 x (18 + 15) - 1,450) / 18 = 102.7778: 15 open at that + 1,450 = 2,991.67 against the broker's 3,000 (first-in first-out against the average-cost method).
        var rebuild = result.Report.Securities.Single().Rebuild;
        s.OpenCost().ShouldBe(2991.667m, 0.001m);
        rebuild.OpenCostAfter!.Value.ShouldBe(2991.667m, 0.001m);
        (rebuild.BrokerCost, rebuild.CostNote).ShouldBe((3000m, null));
        rebuild.CostDelta!.Value.ShouldBe(-8.333m, 0.001m);
    }

    [Fact]
    public async Task With_no_sales_in_the_file_the_open_cost_is_the_brokers_to_the_rounding_of_the_opening_unit_cost()
    {
        var s = await StartAsync();

        var result = await s.ImportOkAsync(Csv(BaseRows[0], BaseRows[1]));

        var rebuild = result.Report.Securities.Single().Rebuild;
        s.OpenQty().ShouldBe(30);
        s.OpenCost().ShouldBe(3000m, 0.01m);
        rebuild.CostDelta!.Value.ShouldBe(0m, 0.01m);
        rebuild.BrokerCost.ShouldBe(3000m);
    }

    [Fact]
    public async Task The_cost_figures_are_in_the_digest()
    {
        var s = await StartAsync();
        var one = (await s.PreviewOkAsync(Csv(BaseRows))).Digest;
        var other = (await s.PreviewOkAsync(Csv(BaseRows[0], BaseRows[1], "2024-09-20,EXMPL,INE000A01011,sell,3,131,T3"))).Digest;

        other.ShouldNotBe(one);
    }

    [Fact]
    public async Task The_preview_stores_nothing_and_changes_no_lot()
    {
        var s = await StartAsync();
        var saves = s.Fx.Data.SaveCount;
        var before = Shape(s.Holding);

        var report = await s.PreviewOkAsync(Csv(BaseRows));

        report.Securities.ShouldHaveSingleItem().Rebuild.Status.ShouldBe("ok");
        s.Data.Trades.ShouldBeEmpty();
        s.Data.Batches.ShouldBeEmpty();
        s.Fx.Data.SaveCount.ShouldBe(saves);
        Shape(s.Holding).ShouldBe(before);
        s.Fx.Events(AccountEventType.TradesImported).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_reports_the_file_the_securities_and_the_rebuild()
    {
        var s = await StartAsync();

        var report = await s.PreviewOkAsync(Csv([.. BaseRows, "2024-04-02,ACME,INE000B01019,buy,100,95.4,T9", "2024-04-03,NIFTY24APRFUT,,buy,50,100,T10"]));

        report.File.ShouldSatisfyAllConditions(
            f => f.Rows.ShouldBe(5),
            f => f.Parsed.ShouldBe(4),
            f => f.Skipped.ByReason.ShouldBe(new Dictionary<string, int> { ["derivatives"] = 1 }),
            f => f.BadTotal.ShouldBe(0),
            f => f.AlreadyImported.ShouldBe(0),
            f => f.NewTrades.ShouldBe(4));
        var exmpl = report.Securities.Single(x => x.Symbol == "EXMPL");
        (exmpl.Matched, exmpl.HoldingId, exmpl.Trades, exmpl.Buys, exmpl.Sells, exmpl.FirstTradeDate, exmpl.LastTradeDate).ShouldBe((true, s.Holding.Id, 3, 2, 1, D("2024-03-05"), D("2024-09-20")));
        exmpl.Rebuild.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe("ok"),
            r => r.LotsBefore.ShouldBe(1),
            r => r.LotsAfter.ShouldBe(4),
            r => r.HandDatedLotsReplaced.ShouldBe(0),
            r => r.SyncLotsReplaced.ShouldBe(0),
            r => r.RealisedPnl.ShouldBe(3 * (130m - 102.7778m)),
            r => r.AdjustedFor.ShouldBe(0),
            r => r.Q.ShouldBe(30),
            r => r.Net.ShouldBe(12),
            r => r.OpeningQty.Before.ShouldBe(30),
            r => r.OpeningQty.After.ShouldBe(18),
            r => r.Digest.Length.ShouldBe(64));
        var acme = report.Securities.Single(x => x.Symbol == "ACME");
        (acme.Matched, acme.HoldingId, acme.Rebuild.Status).ShouldBe((false, null, "no_holding"));
        (report.NotHeld.Securities, report.NotHeld.Trades, report.Unmatched.Securities, report.Unmatched.Trades).ShouldBe((1, 1, 1, 1));
        report.Digest.Length.ShouldBe(64);
    }

    [Fact]
    public async Task A_security_of_the_master_list_that_is_not_held_is_not_held_but_not_unmatched()
    {
        var s = await StartAsync();
        s.Data.Instruments.Add(new XDrishti.Application.Abstractions.Portfolio.TradeInstrument(Guid.NewGuid(), "ACME", "INE000B01019", "share"));

        var report = await s.PreviewOkAsync(Csv("2024-04-02,ACME,INE000B01019,buy,100,95.4,T9"));

        var acme = report.Securities.ShouldHaveSingleItem();
        (acme.Matched, acme.Rebuild.Status).ShouldBe((true, "no_holding"));
        (report.NotHeld.Securities, report.Unmatched.Securities).ShouldBe((1, 0));
    }

    [Fact]
    public async Task Hand_dated_and_sync_added_lots_that_would_be_replaced_are_counted()
    {
        var s = await StartAsync();
        var lot = s.Holding.Lots.Single();
        s.Holding.ChangeLotDate(lot.Id, D("2023-02-02"), s.Fx.Today).IsSuccess.ShouldBeTrue();
        s.Fx.Dhan.Rows[0] = Row("EXMPL", 35, 102m, 120m, "1001", Isin);
        Ok(await s.Fx.SyncAsync(s.Account.Id));

        var rebuild = (await s.PreviewOkAsync(Csv(BaseRows))).Securities.Single().Rebuild;

        (rebuild.LotsBefore, rebuild.HandDatedLotsReplaced, rebuild.SyncLotsReplaced).ShouldBe((2, 1, 1));
    }

    [Fact]
    public async Task Importing_the_same_file_again_stores_nothing_makes_no_batch_and_changes_nothing()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        var lots = Shape(s.Holding);
        var events = s.Fx.Accounts.Events.Count;

        var again = await s.ImportOkAsync(Csv(BaseRows));

        (again.Imported, again.AlreadyImported, again.Batch, again.HoldingsRebuilt).ShouldBe((0, 3, null, 0));
        again.Report.Securities.Single().Rebuild.Status.ShouldBe("unchanged");
        s.Data.Trades.Count.ShouldBe(3);
        s.Data.Batches.Count.ShouldBe(1);
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Accounts.Events.Count.ShouldBe(events);
    }

    [Fact]
    public async Task A_file_with_a_trade_added_stores_only_the_new_trade_and_rebuilds_from_all()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));

        var result = await s.ImportOkAsync(Csv([.. BaseRows, "2024-11-02,EXMPL,INE000A01011,sell,2,140,T4"]));

        (result.Imported, result.AlreadyImported, result.HoldingsRebuilt).ShouldBe((1, 3, 1));
        s.Data.Trades.Count.ShouldBe(4);
        s.Data.Batches.Count.ShouldBe(2);
        s.Holding.Lots.Count(l => l.ClosedOn is not null).ShouldBe(2);
    }

    [Fact]
    public async Task An_import_without_a_rebuild_stores_the_trades_and_touches_no_lot()
    {
        var s = await StartAsync();
        var lots = Shape(s.Holding);

        var result = await s.ImportOkAsync(Csv(BaseRows), rebuild: false);

        (result.Imported, result.Rebuilt, result.HoldingsRebuilt).ShouldBe((3, false, 0));
        Shape(s.Holding).ShouldBe(lots);
        s.Fx.Events(AccountEventType.LotsRebuilt).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rebuild_needs_the_digest_of_the_preview()
    {
        var s = await StartAsync();

        var result = await s.Fx.ImportTrades().HandleAsync(new ImportTradesCommand(s.Account.Id, "t.csv", Content(Csv(BaseRows)), Rebuild: true, null, Actor), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        PortfolioFixture.FieldsOf(result).ShouldBe(["digest"]);
        s.Data.Trades.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_lots_changed_since_the_preview_the_import_is_refused_with_the_fresh_report_and_nothing_is_applied()
    {
        var s = await StartAsync();
        var digest = (await s.PreviewOkAsync(Csv(BaseRows))).Digest;
        s.Holding.ChangeLotDate(s.Holding.Lots.Single().Id, D("2023-02-02"), s.Fx.Today).IsSuccess.ShouldBeTrue();
        var lots = Shape(s.Holding);

        var result = await s.ImportAsync(Csv(BaseRows), digest: digest);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.preview_stale");
        var report = result.Error.Payload!["report"].ShouldBeOfType<TradePreviewReport>();
        report.Digest.ShouldNotBe(digest);
        report.Securities.Single().Rebuild.HandDatedLotsReplaced.ShouldBe(1);
        s.Data.Trades.ShouldBeEmpty();
        s.Data.Batches.ShouldBeEmpty();
        Shape(s.Holding).ShouldBe(lots);
    }

    [Fact]
    public async Task When_a_sync_changed_the_quantity_since_the_preview_the_import_is_refused()
    {
        var s = await StartAsync();
        var digest = (await s.PreviewOkAsync(Csv(BaseRows))).Digest;
        s.Fx.Dhan.Rows[0] = Row("EXMPL", 31, 100m, 120m, "1001", Isin);
        Ok(await s.Fx.SyncAsync(s.Account.Id));

        var result = await s.ImportAsync(Csv(BaseRows), digest: digest);

        result.Error.Code.ShouldBe("portfolio.preview_stale");
    }

    [Fact]
    public async Task When_a_corporate_action_was_applied_since_the_preview_the_import_is_refused()
    {
        var s = await StartAsync();
        s.Fx.Instruments.Known["INE000A01011"] = new XDrishti.Application.Abstractions.Market.InstrumentRef(Guid.NewGuid(), "NSE:EXMPL", "Example");
        s.Fx.Dhan.Rows[0] = Row("EXMPL", 30, 100m, 120m, "1001", Isin);
        Ok(await s.Fx.SyncAsync(s.Account.Id));
        var digest = (await s.PreviewOkAsync(Csv(BaseRows))).Digest;
        s.Data.Actions[s.Holding.InstrumentId!.Value] = [Split("2024-10-01", 1, 2)];

        var result = await s.ImportAsync(Csv(BaseRows), digest: digest);

        result.Error.Code.ShouldBe("portfolio.preview_stale");
    }

    private static XDrishti.Domain.Market.Actions.AppliedAction Split(string exDate, int oldShares, int newShares) => ReconFixtures.Split(exDate, oldShares, newShares);

    [Fact]
    public async Task A_split_between_the_trades_and_today_is_applied_to_the_trades_and_the_preview_says_how_many()
    {
        var s = await StartAsync(qty: 40, avgCost: 50m);
        s.Fx.Instruments.Known["INE000A01011"] = new XDrishti.Application.Abstractions.Market.InstrumentRef(Guid.NewGuid(), "NSE:EXMPL", "Example");
        Ok(await s.Fx.SyncAsync(s.Account.Id));
        s.Data.Actions[s.Holding.InstrumentId!.Value] = [Split("2024-10-01", 1, 2)];

        var report = await s.PreviewOkAsync(Csv("2024-03-05,EXMPL,INE000A01011,buy,10,100,T1"));

        var rebuild = report.Securities.Single().Rebuild;
        (rebuild.Status, rebuild.AdjustedFor, rebuild.Net, rebuild.OpeningQty.After).ShouldBe(("ok", 1, 20L, 20L));
    }

    [Fact]
    public async Task The_corporate_actions_are_read_once_per_request()
    {
        var s = await StartAsync();

        await s.PreviewOkAsync(Csv(BaseRows));

        s.Data.ActionReads.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task While_a_sync_of_the_account_runs_the_import_is_refused()
    {
        var s = await StartAsync();
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, Actor, s.Fx.Clock.GetUtcNow()));

        var result = await s.ImportAsync(Csv(BaseRows));

        result.Error.Code.ShouldBe("portfolio.sync_running");
        s.Data.Trades.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BrokerSyncKind.Trades)]
    [InlineData(BrokerSyncKind.Ledger)]
    public async Task While_a_broker_history_run_of_the_account_runs_the_import_is_refused(BrokerSyncKind kind)
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, kind, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var result = await s.ImportAsync(Csv(BaseRows));

        result.Error.Code.ShouldBe("portfolio.sync_running");
        s.Data.Trades.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_broker_history_run_of_another_account_does_not_hold_the_import_back()
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(Guid.NewGuid(), BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var result = await s.ImportAsync(Csv(BaseRows));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_holding_the_trades_do_not_fit_is_reported_stored_and_left_alone()
    {
        var s = await StartAsync(qty: 10);
        var lots = Shape(s.Holding);

        var result = await s.ImportOkAsync(Csv("2024-03-05,EXMPL,INE000A01011,buy,15,90,T1"));

        var rebuild = result.Report.Securities.Single().Rebuild;
        (rebuild.Status, rebuild.Reason, rebuild.Q, rebuild.Net, rebuild.OpeningQty.After).ShouldBe(("quantity_mismatch", "negative_opening", 10L, 15L, -5L));
        (result.Imported, result.HoldingsRebuilt).ShouldBe((1, 0));
        Shape(s.Holding).ShouldBe(lots);
    }

    [Fact]
    public async Task Trades_of_a_security_the_account_does_not_hold_are_stored_and_not_applied()
    {
        var s = await StartAsync();
        var lots = Shape(s.Holding);

        var result = await s.ImportOkAsync(Csv("2024-04-02,ACME,INE000B01019,buy,100,95.4,T9"));

        (result.Imported, result.HoldingsRebuilt, result.Report.NotHeld.Trades).ShouldBe((1, 0, 1));
        s.Data.Trades.ShouldHaveSingleItem().TradingSymbol.ShouldBe("ACME");
        Shape(s.Holding).ShouldBe(lots);
    }

    [Fact]
    public async Task A_security_that_two_holdings_match_is_ambiguous_and_not_applied()
    {
        var s = await StartAsync();
        s.Fx.Dhan.Rows.AddRange([Row("TWINS", 10, 50m, 55m, "2001", null), new("BSE", "2002", null, "TWINS", 5, 60m, 61m)]);
        Ok(await s.Fx.SyncAsync(s.Account.Id));

        var result = await s.ImportOkAsync("date,symbol,side,quantity,price,trade id\n2024-03-05,TWINS,buy,10,50,T1\n");

        var security = result.Report.Securities.ShouldHaveSingleItem();
        (security.Rebuild.Status, security.HoldingId, security.Matched).ShouldBe(("ambiguous", null, true));
        (result.Imported, result.HoldingsRebuilt).ShouldBe((1, 0));
    }

    [Fact]
    public async Task The_import_is_audited_with_counts_and_no_symbols_quantities_or_prices()
    {
        var s = await StartAsync();

        await s.ImportOkAsync(Csv(BaseRows));

        var imported = s.Fx.Events(AccountEventType.TradesImported).ShouldHaveSingleItem();
        imported.Detail.ShouldBe("Trades imported: 3 valid rows, 3 stored, 0 already imported or repeated, 0 skipped, 0 bad");
        var rebuilt = s.Fx.Events(AccountEventType.LotsRebuilt).ShouldHaveSingleItem();
        rebuilt.Detail.ShouldBe("Lots rebuilt from trades: 1 holding, 1 lots replaced by 4 (0 hand-dated, 0 added by syncs)");
        (imported.AccountId, imported.Actor).ShouldBe((s.Account.Id, Actor));
        new[] { imported.Detail, rebuilt.Detail }.ShouldAllBe(d => !d!.Contains("EXMPL") && !d.Contains("130"));
    }

    [Fact]
    public async Task Rebuilding_twice_gives_the_same_lots()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Csv(BaseRows));
        var first = Shape(s.Holding);

        var again = Ok(await s.RebuildAsync(dryRun: false));

        again.Applied.ShouldBeFalse();
        again.Rebuild.Status.ShouldBe("unchanged");
        Shape(s.Holding).ShouldBe(first);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_refused_and_nothing_is_stored()
    {
        var s = await StartAsync();

        var result = await s.PreviewAsync("when,what\n1,2\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        PortfolioFixture.FieldsOf(result).ShouldBe(["file"]);
    }

    [Fact]
    public async Task An_unknown_account_and_an_account_without_the_portfolio_role_are_refused()
    {
        var s = await StartAsync();
        var plain = await s.Fx.Connections.Accounts.CreatedAsync(Inputs.Account(name: "Plain", clientId: "1100000002", roles: Inputs.Roles(false, false)));

        var unknown = await s.PreviewAsync(Csv(BaseRows), Guid.NewGuid());
        var noRole = await s.PreviewAsync(Csv(BaseRows), plain.Id);

        unknown.Error.Code.ShouldBe("portfolio.not_found");
        noRole.Error.Code.ShouldBe("portfolio.role_off");
    }
}
