using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// The global transaction report as trades (ADR 0029, P2-17 criteria 6, 7, 9, 12 and 13) through the real preview and import handlers on in-memory ports: the account holds 30 shares of Zenith (ZENITHE), which the report names ZENITH ENERGY SYSTEM LTD,
/// and the report has the buys of 100 at 10 and 50 at 12 and the sale of 120 at 15, so that the trades explain the holding exactly.
/// </summary>
public sealed class BillImportTests
{
    private const string Name = "ZENITH ENERGY SYSTEM LTD";

    private static readonly string Buy1 = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 100, "1000.00", 0, "0.00", brokerage: "1.00");

    private static readonly string Buy2 = GtrRow("20 Apr 2026 00:00:00", Name, "NSE", "1002", 50, "600.00", 0, "0.00");

    private static readonly string Sell = GtrRow("04 May 2026 00:00:00", Name, "NSE", "1003", 0, "0.00", 120, "1800.00", brokerage: "2.00");

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync(s =>
    {
        s.Hold("ZENITHE", Zenith, 30, 12m);
        s.ListSampleCompanies();
    });

    [Fact]
    public async Task A_bill_number_used_on_the_nse_and_on_the_bse_for_the_same_scrip_and_day_stores_both_trades_and_importing_again_stores_nothing()
    {
        var s = await StartAsync();
        var nse = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 100, "1000.00", 0, "0.00");
        var bse = GtrRow("07 Apr 2026 00:00:00", Name, "BSE", "1001", 10, "100.00", 0, "0.00");
        var text = Gtr([nse, bse]);

        var done = await s.ImportOkAsync(text, rebuild: false);
        var again = await s.ImportOkAsync(text, rebuild: false);

        done.Trades!.Imported.ShouldBe(2);
        s.Active().Where(t => t.Source == TradeSource.Bill).Select(t => (t.Exchange, t.Qty)).Order().ShouldBe([("BSE", 10L), ("NSE", 100L)]);
        s.Active().Select(t => t.ExternalId).Distinct().Count().ShouldBe(2);
        again.Trades!.Imported.ShouldBe(0);
    }

    [Fact]
    public async Task The_same_nse_and_bse_pair_in_the_other_order_has_the_same_identities_and_importing_it_after_the_first_order_stores_nothing()
    {
        var s = await StartAsync();
        var nse = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 100, "1000.00", 0, "0.00");
        var bse = GtrRow("07 Apr 2026 00:00:00", Name, "BSE", "1001", 10, "100.00", 0, "0.00");
        var forward = await s.PreviewOkAsync(Gtr([nse, bse]));
        var backward = await s.PreviewOkAsync(Gtr([bse, nse]));

        await s.ImportOkAsync(Gtr([nse, bse]), rebuild: false);
        var stored = s.Active().Select(t => t.ExternalId).Order().ToList();
        var reversed = await s.ImportOkAsync(Gtr([bse, nse]), rebuild: false);

        forward.Trades!.File.NewTrades.ShouldBe(2);
        backward.Trades!.File.NewTrades.ShouldBe(2);
        reversed.Trades!.Imported.ShouldBe(0);
        s.Active().Select(t => t.ExternalId).Order().ShouldBe(stored);
        s.Active().Select(t => (t.Exchange, t.Qty)).Order().ShouldBe([("BSE", 10L), ("NSE", 100L)]);
    }

    [Fact]
    public async Task The_identity_set_of_a_colliding_pair_does_not_depend_on_the_order_of_the_rows()
    {
        var first = await StartAsync();
        var second = await StartAsync();
        var nse = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 100, "1000.00", 0, "0.00");
        var bse = GtrRow("07 Apr 2026 00:00:00", Name, "BSE", "1001", 10, "100.00", 0, "0.00");

        await first.ImportOkAsync(Gtr([nse, bse]), rebuild: false);
        await second.ImportOkAsync(Gtr([bse, nse]), rebuild: false);

        first.Active().Select(t => (t.Exchange, t.Qty, t.ExternalId)).Order().ShouldBe(second.Active().Select(t => (t.Exchange, t.Qty, t.ExternalId)).Order());
    }

    [Fact]
    public async Task A_row_without_an_exchange_and_an_nse_row_with_the_same_bill_number_are_both_stored_in_either_order()
    {
        var s = await StartAsync();
        var nse = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 100, "1000.00", 0, "0.00");
        var blank = GtrRow("07 Apr 2026 00:00:00", Name, "", "1001", 10, "100.00", 0, "0.00");

        var done = await s.ImportOkAsync(Gtr([blank, nse]), rebuild: false);
        var reversed = await s.ImportOkAsync(Gtr([nse, blank]), rebuild: false);

        done.Trades!.Imported.ShouldBe(2);
        reversed.Trades!.Imported.ShouldBe(0);
        s.Active().Select(t => t.ExternalId).Distinct().Count().ShouldBe(2);
        s.Active().Select(t => t.Qty).Order().ShouldBe([10L, 100L]);
    }

    [Fact]
    public async Task While_a_broker_history_run_of_the_account_runs_the_import_is_rejected_with_sync_running_and_nothing_is_stored()
    {
        var s = await StartAsync();
        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell]));
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var result = Ok(await s.ImportAsync(Gtr([Buy1, Buy2, Sell]), digest: preview.Digest));

        (result.Status, result.Error!.Code, result.Error.Message).ShouldBe(("rejected", "portfolio.sync_running", PortfolioErrors.SyncRunningWait.Message));
        s.Active().ShouldBeEmpty();
    }

    [Fact]
    public async Task While_only_a_ledger_run_of_the_account_runs_the_bill_import_goes_ahead()
    {
        var s = await StartAsync();
        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell]));
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, BrokerSyncKind.Ledger, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var result = Ok(await s.ImportAsync(Gtr([Buy1, Buy2, Sell]), digest: preview.Digest));

        result.Status.ShouldBe("imported");
        s.Active().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_preview_says_what_would_be_stored_and_stores_nothing()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell]));

        preview.Kind.ShouldBe("global_transaction_report");
        preview.Supported.ShouldBeTrue();
        preview.Error.ShouldBeNull();
        (preview.Summary!.Rows, preview.Summary.ToAdd, preview.Summary.AlreadyImported, preview.Summary.Skipped, preview.Summary.Bad).ShouldBe((3, 3, 0, 0, 0));
        preview.Summary.From.ShouldBe(new DateOnly(2026, 4, 1));
        preview.Digest.ShouldNotBeNullOrEmpty();
        preview.Trades!.File.NewTrades.ShouldBe(3);
        preview.Trades.Securities.Single().Rebuild.Status.ShouldBe("ok");
        s.Active().ShouldBeEmpty();
        s.Fx.Trades.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_name_is_resolved_by_a_partial_match_that_the_preview_flags()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell]));

        var name = preview.Bill!.Names.Single();
        (name.Status, name.Kind, name.Isin, name.Symbol, name.Rows, name.Trades).ShouldBe(("resolved", "partial", Zenith, "ZENITHE", 3, 3));
        preview.Warnings.ShouldContain(w => w.Code == "partial_name_match");
        preview.Warnings.ShouldNotContain(w => w.Code == "needs_match");
    }

    [Fact]
    public async Task An_import_stores_the_bills_as_trades_with_price_value_over_quantity_charges_and_the_bill_source()
    {
        var s = await StartAsync();

        var done = await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]));

        done.Status.ShouldBe("imported");
        done.Trades!.Imported.ShouldBe(3);
        var trades = s.Active().OrderBy(t => t.TradeDate).ToList();
        trades.Select(t => (t.Side, t.Qty, t.Price, t.Charges)).ShouldBe([(TradeSide.Buy, 100L, 10m, 1m), (TradeSide.Buy, 50L, 12m, 0m), (TradeSide.Sell, 120L, 15m, 2m)]);
        trades.ShouldAllBe(t => t.Source == TradeSource.Bill && t.Isin == Zenith && t.TradingSymbol == "ZENITHE" && t.Exchange == "NSE" && t.BatchId == done.Trades!.Batch!.Id);
        trades.ShouldAllBe(t => t.ExternalId.StartsWith("g:", StringComparison.Ordinal));
        done.Trades.Batch!.FileName.ShouldBe("report.csv");
    }

    [Fact]
    public async Task The_lots_are_rebuilt_from_the_bills_and_the_first_in_first_out_profit_is_the_one_worked_out_by_hand()
    {
        var s = await StartAsync();

        var done = await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]));

        // 100 bought at 10 and 50 at 12; 120 sold at 15: the 100 at 10 and 20 of the 50 at 12 go. Cost 1000 + 240 = 1240, proceeds 1800: profit 560. 30 shares at 12 stay, which the broker holds.
        var security = done.Trades!.Report.Securities.Single();
        security.Rebuild.RealisedPnl.ShouldBe(560m);
        (security.Rebuild.Status, security.Rebuild.Q, security.Rebuild.Net).ShouldBe(("ok", 30L, 30L));
        done.Trades.Rebuilt.ShouldBeTrue();
        done.Trades.HoldingsRebuilt.ShouldBe(1);
        var lots = s.Fx.HoldingOf("ZENITHE").Lots;
        lots.Where(l => l.IsOpen).Sum(l => l.Qty).ShouldBe(30);
        lots.Where(l => l.IsOpen).Sum(l => l.Qty * l.UnitCost).ShouldBe(360m);
        lots.Where(l => !l.IsOpen).Sum(l => (l.ClosePrice!.Value - l.UnitCost) * l.Qty).ShouldBe(560m);
    }

    [Fact]
    public async Task Importing_the_same_file_again_stores_nothing_and_makes_no_batch()
    {
        var s = await StartAsync();
        var text = Gtr([Buy1, Buy2, Sell]);
        await s.ImportOkAsync(text);

        var again = await s.ImportOkAsync(text);

        again.Status.ShouldBe("nothing_new");
        (again.Trades!.Imported, again.Trades.AlreadyImported, again.Trades.Batch).ShouldBe((0, 3, null));
        s.Active().Count.ShouldBe(3);
        s.Fx.Trades.Batches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_corrected_file_adds_only_what_is_new()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Gtr([Buy1, Buy2]));

        var corrected = await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]));

        (corrected.Trades!.Imported, corrected.Trades.AlreadyImported).ShouldBe((1, 2));
        s.Active().Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_same_trades_from_another_report_with_the_name_written_with_another_suffix_are_the_same_trades()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]));
        var respelt = Gtr([Buy1.Replace(Name, "Zenith Energy System", StringComparison.Ordinal), Buy2.Replace(Name, "ZENITH ENERGY SYSTEM LIMITED", StringComparison.Ordinal)]);

        var again = await s.ImportOkAsync(respelt);

        again.Trades!.AlreadyImported.ShouldBe(2);
        again.Trades.Imported.ShouldBe(0);
    }

    [Fact]
    public async Task Importing_after_an_undo_stores_the_trades_again()
    {
        var s = await StartAsync();
        var text = Gtr([Buy1, Buy2, Sell]);
        var first = await s.ImportOkAsync(text);
        Ok(await s.UndoTradesAsync(first.Trades!.Batch!.Id));
        s.Active().ShouldBeEmpty();

        var second = await s.ImportOkAsync(text);

        second.Trades!.Imported.ShouldBe(3);
        s.Active().Count.ShouldBe(3);
    }

    [Fact]
    public async Task Undo_voids_the_batch_and_rebuilds_the_lots_from_what_is_left()
    {
        var s = await StartAsync();
        var done = await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]));

        var undone = Ok(await s.UndoTradesAsync(done.Trades!.Batch!.Id));

        undone.Voided.ShouldBe(3);
        s.Fx.Trades.Trades.ShouldAllBe(t => t.IsVoided);
        s.Fx.HoldingOf("ZENITHE").Lots.Where(l => l.IsOpen).Sum(l => l.Qty).ShouldBe(30);
    }

    [Fact]
    public async Task A_rebuild_needs_the_digest_of_the_preview_and_a_stale_one_stores_nothing_and_comes_with_the_fresh_preview()
    {
        var s = await StartAsync();
        var text = Gtr([Buy1, Buy2, Sell]);

        var missing = await s.ImportAsync(text, noDigest: true);
        var stale = await s.ImportAsync(text, digest: "not-the-digest");

        Ok(missing).Error!.Field.ShouldBe("digest");
        Ok(missing).Status.ShouldBe("rejected");
        var rejected = Ok(stale);
        (rejected.Status, rejected.Error!.Code).ShouldBe(("rejected", "portfolio.preview_stale"));
        rejected.Preview!.Digest.ShouldNotBeNullOrEmpty();
        rejected.Preview.Kind.ShouldBe("global_transaction_report");
        s.Active().ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_rebuild_the_trades_are_stored_with_no_digest_and_the_lots_stay()
    {
        var s = await StartAsync();
        var before = s.Fx.HoldingOf("ZENITHE").Lots.Select(l => (l.Qty, l.UnitCost)).ToList();

        var done = Ok(await s.ImportAsync(Gtr([Buy1, Buy2, Sell]), rebuild: false, noDigest: true));

        done.Status.ShouldBe("imported");
        (done.Trades!.Rebuilt, done.Trades.HoldingsRebuilt).ShouldBe((false, 0));
        s.Active().Count.ShouldBe(3);
        s.Fx.HoldingOf("ZENITHE").Lots.Select(l => (l.Qty, l.UnitCost)).ShouldBe(before);
    }

    [Fact]
    public async Task The_rebuild_is_the_explicit_step_of_the_trade_import_and_is_audited_with_counts_only()
    {
        var s = await StartAsync();

        await s.ImportOkAsync(Gtr([Buy1, Buy2, Sell]));

        s.Fx.Events(AccountEventType.TradesImported).Single().Detail.ShouldBe("Trades imported from a Dhan report: 3 valid rows, 3 stored, 0 already imported or repeated, 0 skipped, 0 bad");
        s.Fx.Events(AccountEventType.LotsRebuilt).Single().Detail!.ShouldContain("1 holding");
    }

    [Fact]
    public async Task A_row_with_a_buy_and_a_sell_quantity_becomes_two_trades_buy_first_with_the_charges_split_by_value()
    {
        var s = await StartAsync();
        var mixed = GtrRow("08 May 2026 00:00:00", Name, "NSE", "1004", 10, "100.00", 10, "150.00", brokerage: "5.00");

        await s.ImportOkAsync(Gtr([mixed]), rebuild: false);

        var trades = s.Active().OrderBy(t => t.Seq).ThenBy(t => t.Side).ToList();
        trades.Select(t => (t.Side, t.Qty, t.Price)).ShouldBe([(TradeSide.Buy, 10L, 10m), (TradeSide.Sell, 10L, 15m)]);
        trades.Select(t => t.Charges).ShouldBe([2m, 3m]);
        trades.Select(t => t.ExternalId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Futures_options_commodity_and_unheld_bse_rows_are_counted_per_reason_never_silently_dropped()
    {
        var s = await StartAsync();
        var text = Gtr(
        [
            Buy1,
            GtrRow("07 Apr 2026 00:00:00", "FUT NIFTY 28 Apr 2026", "NSE", "2001", 50, "110000.00", 0, "0.00"),
            GtrRow("07 Apr 2026 00:00:00", "OPT BANKNIFTY 28 Apr 2026 CE 52000", "NSE", "2002", 0, "0.00", 30, "2250.00"),
            GtrRow("07 Apr 2026 00:00:00", "GOLDM FUT 05 Jun 2026", "MCX", "2003", 1, "62000.00", 0, "0.00"),
            GtrRow("07 Apr 2026 00:00:00", "OBSCURE MINERALS LTD", "BSE", "2004", 500, "6250.00", 0, "0.00"),
        ]);

        var preview = await s.PreviewOkAsync(text);

        preview.Trades!.File.Skipped.Total.ShouldBe(4);
        preview.Trades.File.Skipped.ByReason["derivatives"].ShouldBe(3);
        preview.Trades.File.Skipped.ByReason["bse"].ShouldBe(1);
        preview.Summary!.Skipped.ShouldBe(4);
        preview.Summary.ToAdd.ShouldBe(1);
    }

    [Fact]
    public async Task A_bse_row_is_imported_when_its_isin_is_one_the_account_holds()
    {
        var s = await ReportScenario.StartAsync(x =>
        {
            x.Hold("KESTREL", Kestrel, 50, 205m);
            x.ListSampleCompanies();
        });
        var nse = GtrRow("13 Apr 2026 00:00:00", "KESTREL FOODS LTD", "NSE", "3001", 200, "41000.00", 0, "0.00");
        var bse = GtrRow("14 Apr 2026 00:00:00", "KESTREL FOODS LTD", "BSE", "3002", 50, "10250.00", 0, "0.00");

        var done = await s.ImportOkAsync(Gtr([nse, bse]), rebuild: false);

        done.Trades!.Imported.ShouldBe(2);
        s.Active().Select(t => t.Exchange).Order().ShouldBe(["BSE", "NSE"]);
        s.Active().ShouldAllBe(t => t.Isin == Kestrel && t.TradingSymbol == "KESTREL");
    }

    [Fact]
    public async Task A_report_of_the_sample_shape_previews_with_every_kind_of_row_accounted_for()
    {
        var s = await ReportScenario.StartAsync(x =>
        {
            x.Hold("KESTREL", Kestrel, 50, 205m);
            x.ListSampleCompanies();
        });

        var preview = await s.PreviewOkAsync(SampleText("global-transaction-report-shape.csv"));

        preview.Summary!.Bad.ShouldBe(0);
        (preview.Summary.Rows).ShouldBeGreaterThanOrEqualTo(40);
        preview.Summary.ToAdd.ShouldBe(preview.Trades!.File.NewTrades);
        preview.Trades.File.Skipped.ByReason.Keys.Order().ShouldBe(["bse", "derivatives"]);
        preview.Bill!.Charges.Matches.ShouldBeTrue();
        preview.Bill.NeedsMatch.ShouldBe(1);
        preview.Bill.Names.Single(n => n.Status != "resolved").Name.ShouldBe("UNKNOWN WIDGETS LTD");
        preview.Warnings.Select(w => w.Code).ShouldContain("needs_match");
        (preview.Summary.ToAdd + preview.Summary.Skipped).ShouldBeGreaterThanOrEqualTo(preview.Summary.Rows);
    }

    [Fact]
    public async Task The_charges_that_do_not_add_up_to_the_footer_are_flagged()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Gtr([Buy1, Buy2, Sell], footerCharges: 99m));

        preview.Warnings.ShouldContain(w => w.Code == "charges_differ_from_footer");
        preview.Bill!.Charges.Matches.ShouldBeFalse();
        (preview.Bill.Charges.Total, preview.Bill.Charges.ReportTotal).ShouldBe((3m, 99m));
    }

    [Fact]
    public async Task A_row_with_net_refunded_charges_is_imported_with_none_and_flagged()
    {
        var s = await StartAsync();
        var refund = GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1010", 10, "100.00", 0, "0.00", stt: "-0.50");

        var preview = await s.PreviewOkAsync(Gtr([refund]));

        preview.Warnings.ShouldContain(w => w.Code == "negative_charges");
    }

    [Fact]
    public async Task A_report_for_another_client_code_warns_and_is_not_imported_unless_the_owner_ticks_it()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);
        var text = Gtr([Buy1, Buy2, Sell], code: "9999999999");

        var preview = await s.PreviewOkAsync(text);
        var refused = Ok(await s.ImportAsync(text));
        var accepted = Ok(await s.ImportAsync(text, acceptOtherAccount: true));

        preview.Warnings.ShouldContain(w => w.Code == "other_account");
        (refused.Status, refused.Error!.Code).ShouldBe(("rejected", "portfolio.other_account"));
        accepted.Status.ShouldBe("imported");
        s.Active().Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_report_for_the_client_code_the_account_remembers_has_no_warning()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);

        var preview = await s.PreviewOkAsync(Gtr([Buy1]));

        preview.Warnings.ShouldNotContain(w => w.Code == "other_account");
    }

    [Fact]
    public async Task The_personal_lines_and_the_client_code_never_reach_a_response_an_audit_event_or_a_log()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);
        var text = Gtr([Buy1, Buy2, Sell], code: "9999999999");

        var preview = await s.PreviewOkAsync(text);
        var done = Ok(await s.ImportAsync(text, acceptOtherAccount: true));
        var again = Ok(await s.ImportAsync(text, acceptOtherAccount: true));

        var disclosed = s.Disclosed(preview, done, again);
        disclosed.ShouldNotContain("SENTINEL");
        disclosed.ShouldNotContain("9999999999");
        disclosed.ShouldNotContain(ClientCode);
        s.PreviewLog.Messages.ShouldNotBeEmpty();
        s.ImportLog.Messages.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_file_that_is_not_a_report_of_this_shape_cannot_be_read_as_one()
    {
        var s = await StartAsync();

        var broken = Ok(await s.PreviewAsync(GtrTitle + "\nName,x\n" + GtrHeader.Replace("Bill No.", "Bill", StringComparison.Ordinal) + "\n"));

        broken.Kind.ShouldBe("global_transaction_report");
        broken.Error!.Code.ShouldBe("portfolio.invalid");
        broken.Supported.ShouldBeFalse();
    }

    [Fact]
    public async Task An_account_that_does_not_exist_or_has_the_role_off_fails_the_request()
    {
        var s = await StartAsync();

        var missing = await s.PreviewAsync(Gtr([Buy1]), accountId: Guid.NewGuid());

        missing.IsFailure.ShouldBeTrue();
        missing.Error.Code.ShouldBe("portfolio.not_found");
    }
}
