using System.Net;
using System.Text;
using XDrishti.Api.IntegrationTests.BrokerHistory;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.ReportImports;

/// <summary>
/// Dhan's downloaded reports over HTTP on the real database (ADR 0029, P2-17 criteria 8, 10, 13 and 14): the global transaction report as bill-level trades with the tradebook's Undo, the broker's own trades superseding them, the depository-charges sheet
/// (a legacy .xls and an .xlsx) as account costs with its own Undo, the demat statement as names, and the refusals. Companies, ISINs and amounts are made up.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReportImportsApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private const string Name = "EXMPL LIMITED";

    private static byte[] Sheet(string name)
    {
        using var stream = typeof(ReportImportsApiTests).Assembly.GetManifestResourceStream("Spreadsheets/" + name) ?? throw new InvalidOperationException(name);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string Gtr(Guid account, string code = "1000000001") => ReportsApi.Gtr(
        code,
        ReportsApi.Row("05 Oct 2026", Name, "9001", 10, "900.00", 0, "0.00", "1.50"),
        ReportsApi.Row("06 Oct 2026", Name, "9002", 5, "550.00", 0, "0.00"),
        ReportsApi.Row("08 Oct 2026", Name, "9003", 0, "0.00", 3, "390.00", "0.75"));

    /// <summary>The base trades as Dhan's trade history sends them, with their ISIN (the precedence rule joins the broker's trades and the report's rows by it).</summary>
    private void DhanHasTheBaseTradesWithTheirIsin()
    {
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T1", "EXMPL", "BUY", 10, 90m, D(5), Morning, Isin: TradesApi.Isin));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T2", "EXMPL", "BUY", 5, 110m, D(6), Morning, Isin: TradesApi.Isin));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T3", "EXMPL", "SELL", 3, 130m, D(8), Morning, Isin: TradesApi.Isin));
    }

    // ---- the global transaction report ----

    [Fact]
    public async Task A_global_transaction_report_is_previewed_and_imported_as_bill_level_trades_with_the_lots_rebuilt_and_the_trade_batch_to_undo()
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { ReportsApi.Text("report.csv", Gtr(held.Account.Id)) };

        var preview = await Client.PreviewImportsAsync(held.Account.Id, files, Ct);
        var file = preview.Files.Single();

        (file.Kind, file.Supported, file.Error).ShouldBe(("global_transaction_report", true, null));
        (file.Summary!.Rows, file.Summary.ToAdd, file.Summary.Bad).ShouldBe((3, 3, 0));
        file.Trades!.Securities.Single().Rebuild.Status.ShouldBe("ok");
        file.Bill!.Names.Single().Status.ShouldBe("resolved");
        file.Digest.ShouldNotBeNullOrEmpty();
        (await TransactionsAsync()).ShouldBe(0);

        var done = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();

        (done.Status, done.Trades!.Imported, done.Trades.Rebuilt, done.Trades.HoldingsRebuilt).ShouldBe(("imported", 3, true, 1));
        (await TransactionsAsync("source = 'bill' AND batch_id IS NOT NULL AND run_id IS NULL AND external_id LIKE 'g:%' AND isin = 'INE000A01011'")).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM portfolio.transactions WHERE price = 90.000000 AND charges = 1.5 AND trade_date = '2026-10-05'")).ShouldBe(1);
        var lots = await LotsOfAsync(held.HoldingId);
        lots.Lots.ShouldContain(l => l.AcquiredOn == D(5) && l.Source == "trade");

        var undone = await Client.UndoAsync(done.Trades.Batch!.Id, Ct);
        undone.Voided.ShouldBe(3);
        (await TransactionsAsync("voided_at IS NULL")).ShouldBe(0);
    }

    [Fact]
    public async Task The_same_report_again_stores_nothing_and_the_personal_lines_are_nowhere()
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { ReportsApi.Text("report.csv", Gtr(held.Account.Id)) };
        await Client.ImportReportsAsync(held.Account.Id, files, Ct);

        var again = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();

        (again.Status, again.Trades!.Imported, again.Trades.AlreadyImported).ShouldBe(("nothing_new", 0, 3));
        (await TransactionsAsync()).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE detail LIKE '%SENTINEL%' OR detail LIKE '%1000000001%'")).ShouldBe(0);
        Factory.Logs.All.ShouldAllBe(l => !l.Contains("SENTINEL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_report_for_another_client_code_than_the_first_one_imported_is_refused_in_its_result_unless_ticked_and_the_code_is_only_ever_a_hash()
    {
        var held = await HeldOnTheNseAsync();
        var first = new[] { ReportsApi.Text("first.csv", Gtr(held.Account.Id, "AB12CD34EF")) };
        var other = new[] { ReportsApi.Text("other.csv", Gtr(held.Account.Id, "ZZ99YY88XX")) };
        var unknown = await Client.PreviewImportsAsync(held.Account.Id, other, Ct);
        (await CountAsync("SELECT count(*) FROM accounts.report_client_codes")).ShouldBe(0);
        await Client.ImportReportsAsync(held.Account.Id, first, Ct);
        var preview = await Client.PreviewImportsAsync(held.Account.Id, other, Ct);

        using var refused = await Client.ImportReportsRawAsync(held.Account.Id, other, [preview.Files[0].Digest], Ct);
        using var accepted = await Client.ImportReportsRawAsync(held.Account.Id, other, [preview.Files[0].Digest], Ct, acceptOtherAccount: [0]);

        unknown.Files[0].Warnings.ShouldNotContain(w => w.Code == "other_account");
        preview.Files[0].Warnings.ShouldContain(w => w.Code == "other_account");
        (await refused.ReadAsync<XDrishti.Application.Portfolio.Reports.ImportRunResponse>(Ct)).Files.Single().Error!.Code.ShouldBe("portfolio.other_account");
        (await accepted.ReadAsync<XDrishti.Application.Portfolio.Reports.ImportRunResponse>(Ct)).Files.Single().Status.ShouldNotBe("rejected");
        (await CountAsync("SELECT count(*) FROM accounts.report_client_codes WHERE length(hash) = 64 AND hash = encode(sha256(convert_to('xdrishti.report-client-code.v1|AB12CD34EF', 'UTF8')), 'hex')")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM accounts.report_client_codes")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE detail ILIKE '%AB12CD34EF%' OR detail ILIKE '%ZZ99YY88XX%'")).ShouldBe(0);
        Factory.Logs.All.ShouldAllBe(l => !l.Contains("AB12CD34EF", StringComparison.OrdinalIgnoreCase) && !l.Contains("ZZ99YY88XX", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Bill_rows_are_superseded_when_the_broker_sync_later_stores_the_same_days_and_the_database_keeps_the_reason_and_the_run()
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { ReportsApi.Text("report.csv", Gtr(held.Account.Id)) };
        await Client.ImportReportsAsync(held.Account.Id, files, Ct, rebuild: false);
        (await TransactionsAsync("source = 'bill' AND voided_at IS NULL")).ShouldBe(3);
        DhanHasTheBaseTradesWithTheirIsin();

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        status.Trades.LastRun!.Status.ShouldBe("succeeded");
        (status.Trades.LastRun.Counts.Stored, status.Trades.LastRun.Counts.Superseded, status.Trades.LastRun.Counts.SupersededDiffering).ShouldBe((3, 3, 0));
        (await TransactionsAsync("source = 'bill' AND voided_at IS NOT NULL AND voided_by = 'superseded' AND superseded_by IS NOT NULL")).ShouldBe(3);
        (await TransactionsAsync("source = 'broker' AND voided_at IS NULL")).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM portfolio.transactions t JOIN portfolio.broker_sync_runs r ON r.id = t.superseded_by WHERE r.kind = 'trades'")).ShouldBe(3);
        (await Client.HoldingTradesAsync(held.HoldingId, "", Ct)).Items.Count(t => !t.Voided).ShouldBe(3);
    }

    [Fact]
    public async Task A_report_imported_after_the_broker_sync_leaves_out_what_the_broker_has_and_says_so()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTradesWithTheirIsin();
        await SyncAndWaitAsync(held.Account.Id);
        var files = new[] { ReportsApi.Text("report.csv", Gtr(held.Account.Id)) };

        var preview = (await Client.PreviewImportsAsync(held.Account.Id, files, Ct)).Files.Single();
        var done = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();

        preview.Bill!.Coverage.Covered.ShouldBe(3);
        preview.Trades!.File.Skipped.ByReason["covered_by_dhan"].ShouldBe(3);
        (done.Status, done.Trades!.Imported).ShouldBe(("nothing_new", 0));
        (await TransactionsAsync("source = 'bill'")).ShouldBe(0);
    }

    // ---- the depository-charges sheet ----

    [Theory]
    [InlineData("dp-charges-synthetic.xls")]
    [InlineData("dp-charges-synthetic.xlsx")]
    public async Task A_depository_charges_sheet_in_either_format_becomes_account_costs_in_the_ledger_and_is_undone_by_its_batch(string sample)
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { (sample, Sheet(sample)) };

        var preview = (await Client.PreviewImportsAsync(held.Account.Id, files, Ct)).Files.Single();

        (preview.Kind, preview.Supported).ShouldBe(("dp_charges", true));
        (preview.Costs!.Counts.Read, preview.Costs.Counts.ToStore, preview.Costs.Counts.Zero, preview.Costs.Counts.BuySide).ShouldBe((3, 1, 2, 0));
        preview.Costs.AmountToStore.ShouldBe(12.5m);

        var done = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();

        (done.Status, done.Costs!.Imported, done.Costs.Amount, done.Costs.NamesLearned).ShouldBe(("imported", 1, 12.5m, 2));
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE source = 'import' AND class = 'charges' AND run_id IS NULL AND batch_id IS NOT NULL AND description = 'DP charges' AND amount = -12.5 AND isin = 'INE245A01021' AND quantity = 11 AND entry_date = '2026-06-04'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM portfolio.security_aliases WHERE source = 'dp'")).ShouldBe(2);
        var batches = await Client.CostBatchesAsync($"?accountId={held.Account.Id}", Ct);
        (batches.Total, batches.Items.Single().Status, batches.Items.Single().ActiveEntries).ShouldBe((1, "imported", 1));
        var ledger = await Client.LedgerAsync(held.Account.Id, "?class=charges", Ct);
        ledger.Items.ShouldContain(e => e.Source == "import" && e.Amount == -12.5m);

        var again = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();
        again.Status.ShouldBe("nothing_new");

        var undone = await Client.UndoCostBatchAsync(done.Costs.Batch!.Id, Ct);
        (undone.Voided, undone.Batch.Status, undone.AlreadyUndone).ShouldBe((1, "undone", false));
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE source = 'import' AND voided_at IS NULL")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE source = 'import' AND voided_by IS NOT NULL")).ShouldBe(1);
        var reimport = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();
        reimport.Costs!.Imported.ShouldBe(1);
    }

    [Fact]
    public async Task The_imported_charge_raises_the_net_figures_costs_once_and_undoing_it_takes_it_away_and_a_charge_dhans_ledger_has_is_not_counted_twice()
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { ("dp.xls", Sheet("dp-charges-synthetic.xls")) };

        var done = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();
        var after = await Client.SummaryAsync(string.Empty, Ct);
        await Client.UndoCostBatchAsync(done.Costs!.Batch!.Id, Ct);
        var undone = await Client.SummaryAsync(string.Empty, Ct);

        (after.Income!.LedgerCharges, after.Income.Charges).ShouldBe((12.5m, 12.5m));
        undone.Income.ShouldBeNull();

        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(new DateOnly(2026, 6, 4), "V9", "JOURNAL", "DP CHARGES 04-06-2026 EXMPL", 12.5m, 0m, 100m));
        await SyncAndWaitAsync(held.Account.Id, "2026-06-01");
        var again = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();
        var counted = await Client.SummaryAsync(string.Empty, Ct);

        again.Costs!.AlreadyInLedger.ShouldBe(1);
        (again.Costs.Imported, again.Status).ShouldBe((0, "nothing_new")); // the names the sheet teaches were remembered by the first import; the charge is Dhan's ledger's already
        counted.Income!.LedgerCharges.ShouldBe(12.5m);
    }

    [Fact]
    public async Task A_sheet_imported_before_the_ledger_sync_is_replaced_by_dhans_charge_in_the_database_and_counted_once_and_undoing_the_batch_takes_nothing_more()
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { ("dp.xls", Sheet("dp-charges-synthetic.xls")) };
        var done = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(new DateOnly(2026, 6, 4), "V9", "JOURNAL", "DP CHARGES 04-06-2026 EXMPL", 12.5m, 0m, 100m));

        var (_, status) = await SyncAndWaitAsync(held.Account.Id, "2026-06-01");

        status.Ledger.LastRun!.Status.ShouldBe("succeeded");
        status.Ledger.LastRun.Counts.Superseded.ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE source = 'import' AND voided_at IS NOT NULL AND voided_by = 'superseded'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE class = 'charges' AND voided_at IS NULL")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries WHERE source = 'broker' AND class = 'charges' AND voided_at IS NULL")).ShouldBe(1);
        (await Client.SummaryAsync(string.Empty, Ct)).Income!.LedgerCharges.ShouldBe(12.5m);
        (await Client.CostBatchesAsync($"?accountId={held.Account.Id}", Ct)).Items.Single().ActiveEntries.ShouldBe(0);

        var undone = await Client.UndoCostBatchAsync(done.Costs!.Batch!.Id, Ct);
        undone.Voided.ShouldBe(0);
        (await Client.SummaryAsync(string.Empty, Ct)).Income!.LedgerCharges.ShouldBe(12.5m);
    }

    [Fact]
    public async Task An_xls_sheet_is_recognised_by_its_content_whatever_the_file_is_called()
    {
        var held = await HeldOnTheNseAsync();

        var preview = (await Client.PreviewImportsAsync(held.Account.Id, [("whatever.csv", Sheet("dp-charges-synthetic.xls"))], Ct)).Files.Single();

        preview.Kind.ShouldBe("dp_charges");
    }

    // ---- the demat statement and the other kinds ----

    [Fact]
    public async Task A_demat_statement_is_compared_with_the_holdings_and_teaches_names_without_changing_any_holding()
    {
        var held = await HeldOnTheNseAsync();
        var statement = string.Join('\n',
        [
            "Holding summary,For 12-10-2026",
            "Name,ZZ-SENTINEL-NAME",
            "UCC,1000000001",
            string.Empty,
            "Scrip Name,ISIN Code,Free Holding,Locked In,Safe Keep,MTF Pledge,Margin Pledge,CUSA Pledge,Closing Price,Valuation",
            $"\"EXMPL LIMITED\",{TradesApi.Isin},25,5,0,0,0,0,120.00,3600.00",
            "\"OTHER EXAMPLE LIMITED\",INE777X01077,9,0,0,0,0,0,50.00,450.00",
            "Valuation,4050.00,Total Number of Securities,2",
        ]) + "\n";
        var files = new[] { ReportsApi.Text("demat.csv", statement) };

        var preview = (await Client.PreviewImportsAsync(held.Account.Id, files, Ct)).Files.Single();
        var done = (await Client.ImportReportsAsync(held.Account.Id, files, Ct)).Files.Single();

        (preview.Kind, preview.Demat!.Securities, preview.Demat.Matching, preview.Demat.HeldNotSynced.Single().Isin).ShouldBe(("demat_holding", 2, 1, "INE777X01077"));
        (done.Status, done.Demat!.NamesLearned).ShouldBe(("imported", 2));
        (await CountAsync("SELECT count(*) FROM portfolio.security_aliases WHERE source = 'demat'")).ShouldBe(2);
        (await RowAsync()).Qty.ShouldBe(30);
    }

    [Fact]
    public async Task A_profit_and_loss_statement_and_a_file_of_an_unknown_shape_are_answered_per_file()
    {
        var held = await HeldOnTheNseAsync();
        const string Pnl = "Realised PnL Report,From 01-04-2025 to 31-03-2026\nName,ZZ-SENTINEL-NAME\n\nScrip Name,Quantity,Avg. Buy Price,Buy Value,Avg. Sell Price,Sell Value,Realised P&L,Realised P&L %\nExample,1,1,1,1,1,0,0\n";

        var preview = await Client.PreviewImportsAsync(held.Account.Id, [ReportsApi.Text("pnl.csv", Pnl), ReportsApi.Text("what.csv", "Name,Someone\nFoo,Bar\n"), ReportsApi.Text("empty.csv", string.Empty)], Ct);

        preview.Files.Select(f => f.Kind).ShouldBe(["pnl_report", "unknown", "unknown"]);
        preview.Files[0].Supported.ShouldBeFalse();
        preview.Files[0].Message.ShouldNotBeNull().ShouldContain("statement of results");
        preview.Files[1].Error!.Code.ShouldBe("portfolio.wrong_file_kind");
        preview.Files[2].Error!.Message.ShouldBe("The file is empty.");
    }

    [Fact]
    public async Task Several_files_are_answered_in_order_one_result_each()
    {
        var held = await HeldOnTheNseAsync();
        var files = new[] { ReportsApi.Text("a.csv", Gtr(held.Account.Id)), ("dp.xls", Sheet("dp-charges-synthetic.xls")) };

        var run = await Client.ImportReportsAsync(held.Account.Id, files, Ct);

        run.Files.Select(f => (f.FileName, f.Kind, f.Status)).ShouldBe([("a.csv", "global_transaction_report", "imported"), ("dp.xls", "dp_charges", "imported")]);
    }

    // ---- the existing imports send a report to the right place ----

    [Theory]
    [InlineData("/api/portfolio/trades/preview", "global_transaction_report")]
    [InlineData("/api/portfolio/dividends/preview", "global_transaction_report")]
    public async Task A_report_dropped_on_the_tradebook_or_dividend_import_is_a_wrong_file_kind_that_says_which(string path, string detected)
    {
        var held = await HeldOnTheNseAsync();
        using var content = TradesApi.Upload(held.Account.Id, Gtr(held.Account.Id));

        using var response = await Client.PostAsync(new Uri(path, UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ProblemAsync(Ct);
        problem.GetProperty("code").GetString().ShouldBe("portfolio.wrong_file_kind");
        problem.GetProperty("detected").GetString().ShouldBe(detected);
    }

    [Fact]
    public async Task A_profit_and_loss_statement_dropped_on_the_tradebook_import_is_a_wrong_file_kind_pnl_report()
    {
        var held = await HeldOnTheNseAsync();
        using var content = TradesApi.Upload(held.Account.Id, "Realised PnL Report,From 01-04-2025 to 31-03-2026\nName,x\n");

        using var response = await Client.PostAsync(new Uri("/api/portfolio/trades/preview", UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemAsync(Ct)).GetProperty("detected").GetString().ShouldBe("pnl_report");
    }

    [Fact]
    public async Task A_bad_request_is_a_400_with_the_field()
    {
        var held = await HeldOnTheNseAsync();

        using var noFiles = await Client.PostAsync(new Uri("/api/portfolio/imports/preview", UriKind.Relative), new MultipartFormDataContent { { new StringContent(held.Account.Id.ToString()), "accountId" } }, Ct);
        using var badChoices = new MultipartFormDataContent { { new StringContent(held.Account.Id.ToString()), "accountId" }, { new StringContent("[not json"), "choices" }, { new ByteArrayContent(Encoding.UTF8.GetBytes("a")), "files", "a.csv" } };
        using var badChoicesResponse = await Client.PostAsync(new Uri("/api/portfolio/imports/preview", UriKind.Relative), badChoices, Ct);
        using var unknownAccount = await Client.PreviewImportsRawAsync(Guid.NewGuid(), [ReportsApi.Text("a.csv", "a")], Ct);

        using var tooMany = ReportsApi.Upload(held.Account.Id, Enumerable.Range(0, 21).Select(i => ReportsApi.Text($"f{i}.csv", "a")));
        using var tooManyResponse = await Client.PostAsync(new Uri("/api/portfolio/imports/preview", UriKind.Relative), tooMany, Ct);
        using var badDigests = ReportsApi.Upload(held.Account.Id, [ReportsApi.Text("a.csv", "a")]);
        badDigests.Add(new StringContent("[\"a\", \"b\"]"), "digests");
        using var badDigestsResponse = await Client.PostAsync(new Uri("/api/portfolio/imports/import", UriKind.Relative), badDigests, Ct);

        tooManyResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooManyResponse.ProblemAsync(Ct)).ToString().ShouldContain("At most 20 files");
        badDigestsResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badDigestsResponse.ProblemAsync(Ct)).ToString().ShouldContain("digests");
        noFiles.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noFiles.ProblemAsync(Ct)).ToString().ShouldContain("files");
        badChoicesResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknownAccount.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
