using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Income;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// The depository-charges sheet as account costs (ADR 0029, P2-17 criteria 3, 10 and 12): each charge on a sale becomes a ledger entry of class charges with source import and a batch, that the net figures count once; zero and buy-side lines are listed and not stored; a charge that
/// Dhan's own ledger has is not stored again; Undo voids the batch.
/// </summary>
public sealed class DpImportTests
{
    private const string Isin = "INE245A01021";

    private static readonly string Sell = DpLine(1, "04-Jun-2026", Isin, "THE EXAMPLE POWER COMPANY LIMITED", "11.000", "Sell", "Other Fees", "12.5");

    private static readonly string Zero = DpLine(2, "04-Jun-2026", Isin, "THE EXAMPLE POWER COMPANY LIMITED", "11.000", "Sell", "DP Fees", "0");

    private static readonly string Buy = DpLine(3, "01-Jun-2026", Isin, "THE EXAMPLE POWER COMPANY LIMITED", "11.000", "Buy", "Other Fees", "3");

    private static readonly string OffMarket = DpLine(4, "09-Jun-2026", "INE555B01015", "Another Example Limited", "7.000", "Sell", "Off-Market Fees", "30");

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync();

    [Fact]
    public async Task The_preview_lists_every_line_with_what_happens_to_it_and_stores_nothing()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Dp([Sell, Zero, Buy, OffMarket]));

        preview.Kind.ShouldBe("dp_charges");
        preview.Supported.ShouldBeTrue();
        var costs = preview.Costs!;
        costs.Lines.Select(l => l.Status).ShouldBe(["new", "zero", "buy_side", "new"]);
        (costs.Counts.Read, costs.Counts.ToStore, costs.Counts.Zero, costs.Counts.BuySide, costs.Counts.OffMarket).ShouldBe((4, 2, 1, 1, 1));
        costs.AmountToStore.ShouldBe(42.5m);
        (preview.Summary!.ToAdd, preview.Summary.Skipped, preview.Summary.Rows).ShouldBe((2, 2, 4));
        preview.Summary.From.ShouldBe(new DateOnly(2026, 4, 1));
        s.ActiveCosts().ShouldBeEmpty();
        s.Costs.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_import_stores_each_charge_as_a_ledger_entry_of_class_charges_with_the_source_import_and_the_batch()
    {
        var s = await StartAsync();

        var done = await s.ImportOkAsync(Dp([Sell, Zero, Buy, OffMarket]));

        done.Status.ShouldBe("imported");
        done.Costs!.Imported.ShouldBe(2);
        done.Costs.Amount.ShouldBe(42.5m);
        var entries = s.ActiveCosts().OrderBy(e => e.EntryDate).ToList();
        entries.Count.ShouldBe(2);
        var first = entries[0];
        (first.EntryClass, first.Description, first.Source, first.Amount, first.Isin, first.Quantity, first.EntryDate).ShouldBe((LedgerClass.Charges, "DP charges", LedgerSource.Import, -12.5m, Isin, 11L, new DateOnly(2026, 6, 4)));
        first.RunId.ShouldBeNull();
        first.BatchId.ShouldBe(done.Costs.Batch!.Id);
        first.Narration.ShouldBe("DP charges - Other Fees");
        entries[1].Narration.ShouldBe("DP charges - Off-Market Fees");
        (done.Costs.Batch.Parsed, done.Costs.Batch.Imported, done.Costs.Batch.Duplicates, done.Costs.Batch.Amount, done.Costs.Batch.Status, done.Costs.Batch.ActiveEntries).ShouldBe((4, 2, 2, 42.5m, "imported", 2));
    }

    [Fact]
    public async Task The_entries_are_costs_of_the_account_that_the_net_figures_count_exactly_once()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell, OffMarket]));

        var entries = s.ActiveCosts();

        entries.ShouldAllBe(e => LedgerCosts.IsAccountLevel(e.Description, e.Narration));
        var data = new IncomeData(
            [s.Account.Id],
            [],
            [],
            [],
            [.. entries.GroupBy(e => (e.EntryDate, e.Description, e.Narration)).Select(g => new IncomeLedgerCharge(s.Account.Id, g.Key.EntryDate, g.Key.Description, g.Key.Narration, -g.Sum(e => e.Amount)))],
            []);
        var book = IncomeBook.Build(data);
        book.Account(s.Account.Id).LedgerCharges.Sum(c => c.Amount).ShouldBe(42.5m);
        s.Fx.BrokerData.Entries.Count(e => e.EntryClass == LedgerClass.Charges).ShouldBe(2);
    }

    [Theory]
    [InlineData("Other Fees")]
    [InlineData("DP Fees")]
    [InlineData("Off-Market Fees")]
    [InlineData("Settlement Charges")]
    [InlineData("Bill Related")]
    [InlineData("")]
    public void The_description_and_narration_of_every_type_seen_are_counted_as_an_account_cost(string type)
    {
        var line = new DpLine(2, new DateOnly(2026, 6, 4), Isin, "Example", 11, DpSide.Sell, type, 12.5m);

        LedgerCosts.IsAccountLevel(DpPlanner.Description, DpPlanner.Narration(line)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_costs_do_not_move_the_date_the_ledger_is_known_from()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell]));

        var first = await s.Fx.BrokerData.FirstDateAsync(s.Account.Id, CancellationToken.None);

        first.ShouldBeNull();
    }

    [Fact]
    public async Task Importing_the_same_sheet_again_stores_nothing()
    {
        var s = await StartAsync();
        var text = Dp([Sell, Zero]);
        await s.ImportOkAsync(text);

        var again = await s.ImportOkAsync(text);

        again.Status.ShouldBe("nothing_new");
        (again.Costs!.Imported, again.Costs.AlreadyImported, again.Costs.Batch).ShouldBe((0, 1, null));
        s.ActiveCosts().Count.ShouldBe(1);
        s.Costs.Batches.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_identical_lines_are_both_kept_and_a_corrected_sheet_adds_only_what_is_new()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell, Sell]));

        s.ActiveCosts().Count.ShouldBe(2);
        var corrected = await s.ImportOkAsync(Dp([Sell, Sell, OffMarket]));

        (corrected.Costs!.Imported, corrected.Costs.AlreadyImported).ShouldBe((1, 2));
    }

    [Fact]
    public async Task A_charge_that_equals_one_of_dhans_ledger_the_same_day_is_not_stored_again()
    {
        var s = await StartAsync();
        s.Fx.BrokerData.Add([LedgerEntry.Create(s.Account.Id, new DateOnly(2026, 6, 4), "V1", "DP Charges", "dp charges for 04-Jun", "NSE", -12.5m, LedgerClass.Charges, null, "x1", Guid.NewGuid(), s.Fx.Clock.GetUtcNow())]);

        var preview = await s.PreviewOkAsync(Dp([Sell, OffMarket]));
        var done = await s.ImportOkAsync(Dp([Sell, OffMarket]));

        preview.Costs!.Lines.Select(l => l.Status).ShouldBe(["already_in_ledger", "new"]);
        preview.Costs.Counts.AlreadyInLedger.ShouldBe(1);
        (done.Costs!.Imported, done.Costs.AlreadyInLedger).ShouldBe((1, 1));
        s.ActiveCosts().Single().Isin.ShouldBe("INE555B01015");
    }

    [Fact]
    public async Task A_ledger_charge_covers_one_line_only_not_two_identical_ones()
    {
        var s = await StartAsync();
        s.Fx.BrokerData.Add([LedgerEntry.Create(s.Account.Id, new DateOnly(2026, 6, 4), "V1", "DP Charges", "dp", "NSE", -12.5m, LedgerClass.Charges, null, "x1", Guid.NewGuid(), s.Fx.Clock.GetUtcNow())]);

        var preview = await s.PreviewOkAsync(Dp([Sell, Sell]));

        preview.Costs!.Lines.Select(l => l.Status).ShouldBe(["already_in_ledger", "new"]);
    }

    [Fact]
    public async Task A_ledger_charge_of_another_day_or_amount_or_class_covers_nothing()
    {
        var s = await StartAsync();
        var now = s.Fx.Clock.GetUtcNow();
        s.Fx.BrokerData.Add(
        [
            LedgerEntry.Create(s.Account.Id, new DateOnly(2026, 6, 5), "V1", "DP Charges", "dp", "NSE", -12.5m, LedgerClass.Charges, null, "x1", Guid.NewGuid(), now),
            LedgerEntry.Create(s.Account.Id, new DateOnly(2026, 6, 4), "V2", "DP Charges", "dp", "NSE", -14.75m, LedgerClass.Charges, null, "x2", Guid.NewGuid(), now),
            LedgerEntry.Create(s.Account.Id, new DateOnly(2026, 6, 4), "V3", "Payout", "bank", "NSE", -12.5m, LedgerClass.FundsOut, null, "x3", Guid.NewGuid(), now),
        ]);

        var preview = await s.PreviewOkAsync(Dp([Sell]));

        preview.Costs!.Lines.Single().Status.ShouldBe("new");
    }

    [Fact]
    public async Task Undo_voids_the_batch_and_the_same_sheet_can_be_imported_again()
    {
        var s = await StartAsync();
        var text = Dp([Sell, OffMarket]);
        var done = await s.ImportOkAsync(text);

        var undone = Ok(await s.UndoCostsAsync(done.Costs!.Batch!.Id));

        (undone.Voided, undone.AlreadyUndone, undone.Batch.Status, undone.Batch.ActiveEntries).ShouldBe((2, false, "undone", 0));
        s.ActiveCosts().ShouldBeEmpty();
        s.Fx.BrokerData.Entries.Count.ShouldBe(2);
        Ok(await s.UndoCostsAsync(done.Costs.Batch.Id)).AlreadyUndone.ShouldBeTrue();
        var again = await s.ImportOkAsync(text);
        again.Costs!.Imported.ShouldBe(2);
        s.ActiveCosts().Count.ShouldBe(2);
        s.Fx.Events(AccountEventType.CostsUndone).Single().Detail!.ShouldContain("2 entries voided");
    }

    [Fact]
    public async Task Undoing_a_batch_that_does_not_exist_is_not_found()
    {
        var s = await StartAsync();

        var undone = await s.UndoCostsAsync(Guid.NewGuid());

        undone.Error.Code.ShouldBe("portfolio.not_found");
    }

    [Fact]
    public async Task The_cost_imports_are_listed_newest_first_for_an_account()
    {
        var s = await StartAsync();
        await s.ImportOkAsync(Dp([Sell]));
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        await s.ImportOkAsync(Dp([OffMarket]));

        var page = Ok(await s.ListCostsAsync(s.Account.Id));

        page.Total.ShouldBe(2);
        page.Items.Select(i => i.Amount).ShouldBe([30m, 12.5m]);
        page.Items.ShouldAllBe(i => i.AccountName == s.Account.Name);
        (await s.ListCostsAsync(pageSize: 0)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_names_of_the_securities_are_learned_once_by_the_import()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Dp([Sell, OffMarket]));
        var done = await s.ImportOkAsync(Dp([Sell, OffMarket]));
        var again = await s.ImportOkAsync(Dp([Sell, OffMarket]));

        preview.Costs!.Names.ShouldBe(new NameLearning(0, 2));
        done.Costs!.NamesLearned.ShouldBe(2);
        again.Costs!.NamesLearned.ShouldBe(0);
        s.Learned.Aliases.Select(a => (a.Isin, a.Source)).Order().ShouldBe([("INE245A01021", SecurityAliasSource.Dp), ("INE555B01015", SecurityAliasSource.Dp)]);
    }

    [Fact]
    public async Task A_sheet_with_only_free_lines_still_teaches_its_names_but_stores_no_cost()
    {
        var s = await StartAsync();

        var done = await s.ImportOkAsync(Dp([Zero, Buy]));

        (done.Status, done.Costs!.Imported, done.Costs.NamesLearned, done.Costs.Batch).ShouldBe(("imported", 0, 1, null));
        s.ActiveCosts().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_import_needs_the_digest_and_a_stale_digest_stores_nothing_and_brings_the_fresh_preview()
    {
        var s = await StartAsync();
        var text = Dp([Sell]);

        var missing = Ok(await s.ImportAsync(text, noDigest: true));
        var stale = Ok(await s.ImportAsync(text, digest: "stale"));

        missing.Error!.Field.ShouldBe("digest");
        (stale.Status, stale.Error!.Code, stale.Preview!.Kind).ShouldBe(("rejected", "portfolio.preview_stale", "dp_charges"));
        s.ActiveCosts().ShouldBeEmpty();
        s.Learned.Aliases.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_digest_goes_stale_when_dhans_ledger_gets_the_charge_meanwhile()
    {
        var s = await StartAsync();
        var text = Dp([Sell]);
        var digest = (await s.PreviewOkAsync(text)).Digest;
        s.Fx.BrokerData.Add([LedgerEntry.Create(s.Account.Id, new DateOnly(2026, 6, 4), "V1", "DP Charges", "dp", "NSE", -12.5m, LedgerClass.Charges, null, "x1", Guid.NewGuid(), s.Fx.Clock.GetUtcNow())]);

        var done = Ok(await s.ImportAsync(text, digest: digest));

        done.Error!.Code.ShouldBe("portfolio.preview_stale");
        done.Preview!.Costs!.Lines.Single().Status.ShouldBe("already_in_ledger");
    }

    [Fact]
    public async Task A_sheet_for_another_client_code_warns_and_is_refused_unless_ticked()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);
        var text = Dp([Sell]).Replace(ClientCode, "9999999999", StringComparison.Ordinal);

        var preview = await s.PreviewOkAsync(text);
        var refused = Ok(await s.ImportAsync(text));
        var accepted = Ok(await s.ImportAsync(text, acceptOtherAccount: true));

        preview.Warnings.ShouldContain(w => w.Code == "other_account");
        refused.Error!.Code.ShouldBe("portfolio.other_account");
        accepted.Costs!.Imported.ShouldBe(1);
    }

    [Fact]
    public async Task Nothing_personal_reaches_a_response_an_audit_event_or_a_log_and_the_events_carry_counts_only()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);
        var text = Dp([Sell, OffMarket]).Replace(ClientCode, "9999999999", StringComparison.Ordinal);

        var preview = await s.PreviewOkAsync(text);
        var done = Ok(await s.ImportAsync(text, acceptOtherAccount: true));

        var disclosed = s.Disclosed(preview, done);
        disclosed.ShouldNotContain("SENTINEL");
        disclosed.ShouldNotContain("9999999999");
        s.Fx.Events(AccountEventType.CostsImported).Single().Detail.ShouldNotBeNull().ShouldNotContain("12.5");
    }

    public static TheoryData<BrokerSyncKind> BrokerRunKinds => new() { BrokerSyncKind.Trades, BrokerSyncKind.Ledger };

    [Theory]
    [MemberData(nameof(BrokerRunKinds))]
    public async Task While_a_broker_history_run_of_the_account_runs_the_import_is_rejected_and_nothing_is_stored(BrokerSyncKind kind)
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, kind, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var result = Ok(await s.ImportAsync(Dp([Sell])));

        (result.Status, result.Error!.Code).ShouldBe(("rejected", "portfolio.sync_running"));
        s.ActiveCosts().ShouldBeEmpty();
        s.Costs.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task While_a_holdings_sync_of_the_account_runs_the_import_is_rejected()
    {
        var s = await StartAsync();
        s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, PortfolioFixture.Actor, s.Fx.Clock.GetUtcNow()));

        var result = Ok(await s.ImportAsync(Dp([Sell])));

        (result.Status, result.Error!.Code).ShouldBe(("rejected", "portfolio.sync_running"));
        s.ActiveCosts().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_broker_history_run_of_another_account_does_not_hold_the_import_back()
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(Guid.NewGuid(), BrokerSyncKind.Ledger, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var done = await s.ImportOkAsync(Dp([Sell]));

        done.Costs!.Imported.ShouldBe(1);
    }

    [Fact]
    public async Task The_sheet_can_come_as_a_spreadsheet_read_by_the_port()
    {
        var s = await StartAsync();
        s.Spreadsheets.Rows =
        [
            ["DP Charges | From 01-April-2026 to 15-October-2026"],
            ["Sr.", "Date", "ISIN", "Security Name", "", "Quantity", "Buy/Sell", "Type of Transaction", "", "Charges"],
            ["1", "2026-06-04", Isin, "  THE EXAMPLE POWER COMPANY LIMITED", "", "11", "Sell", "Other Fees", "", "12.5"],
        ];
        var bytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0 };
        var preview = Ok(await new PreviewImportFileHandlerFactory(s).PreviewAsync(bytes));

        preview.Kind.ShouldBe("dp_charges");
        preview.Costs!.Counts.ToStore.ShouldBe(1);
        s.Spreadsheets.Reads.ShouldBe(1);
    }

    /// <summary>Previews raw bytes (a spreadsheet) instead of text.</summary>
    private sealed class PreviewImportFileHandlerFactory(ReportScenario s)
    {
        public Task<XDrishti.Domain.Common.Result<ImportFilePreview>> PreviewAsync(byte[] bytes) =>
            new PreviewImportFileHandler(s.Workspace, s.Previewer, s.Fx.Clock, s.PreviewLog)
                .HandleAsync(new PreviewImportFileQuery(s.Account.Id, "dp.xls", new XDrishti.Application.Portfolio.Trades.TradeFileContent(bytes), []), CancellationToken.None).AsTask();
    }
}
