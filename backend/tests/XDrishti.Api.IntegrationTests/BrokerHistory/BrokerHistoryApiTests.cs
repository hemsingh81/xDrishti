using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// "Sync now" for the trades and ledger over HTTP on the real database and a fake Dhan (P2-13, ADR 0025): what is stored, the rebuild of lots, the watermarks and windows, failures, the audit trail and the ledger list.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerHistoryApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    [Fact]
    public async Task Sync_now_answers_at_once_with_the_running_trades_run_and_the_background_run_stores_trades_ledger_and_lots()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();

        var started = await Client.SyncNowAsync(held.Account.Id, Oct5, Ct);
        var status = await Client.WaitForTheRunAsync(held.Account.Id, started, Ct);

        (started.Trades.Kind, started.Trades.Status, started.Trades.Trigger, started.Trades.From, started.Trades.To).ShouldBe(("trades", "running", "manual", D(5), D(12)));
        started.Ledger.ShouldBeNull();
        (status.Running, status.Trades.Watermark, status.Ledger.Watermark).ShouldBe((false, D(12), D(12)));
        (status.Trades.LastRun!.Status, status.Trades.WindowsDone, status.Trades.WindowsTotal, status.Ledger.LastRun!.Status).ShouldBe(("succeeded", 1, 1, "succeeded"));
        (status.Trades.LastRun.Counts.Stored, status.Trades.LastRun.Counts.Read, status.Ledger.LastRun.Counts.Stored).ShouldBe((3, 3, 5));
        (status.HoldingsRebuilt, status.HoldingsNeedingReview).ShouldBe((1, 0));

        var trades = await Client.HoldingTradesAsync(held.HoldingId, "", Ct);
        trades.Items.Select(t => (t.Side, t.Qty, t.Price, t.TradeDate, t.Source, t.Charges)).ShouldBe(
        [
            ("sell", 3L, 130m, D(8), "broker", 0m),
            ("buy", 5L, 110m, D(6), "broker", 0m),
            ("buy", 10L, 90m, D(5), "broker", 0m),
        ]);
        trades.Items.ShouldAllBe(t => t.BatchId == null && t.CreatedBy == ApiFactory.OwnerEmail);
        (await TransactionsAsync("source = 'broker' AND run_id IS NOT NULL AND batch_id IS NULL AND instrument_id IS NOT NULL")).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM portfolio.transactions WHERE external_id IN ('t:20261005:T1', 't:20261006:T2', 't:20261008:T3')")).ShouldBe(3);

        var lots = await LotsOfAsync(held.HoldingId);
        lots.Lots.Select(l => (l.AcquiredOn, l.Qty, l.Status, l.Source, l.ClosedOn, l.ClosePrice)).ShouldBe(
        [
            (D(4), 15L, "open", "import", (DateOnly?)null, (decimal?)null),
            (D(5), 10L, "open", "trade", null, null),
            (D(6), 5L, "open", "trade", null, null),
            (D(4), 3L, "closed", "import", D(8), 130m),
        ]);
    }

    [Fact]
    public async Task The_calls_to_dhan_are_the_documented_ones_with_the_token_in_the_header_only_and_nothing_secret_is_logged()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();
        var logged = Factory.Logs.All.Count; // the log is the suite's: only what this run wrote counts

        await SyncAndWaitAsync(held.Account.Id);

        TradeRequests.Select(r => r.Uri.AbsolutePath).ShouldBe(["/v2/trades/2026-10-05/2026-10-12/0", "/v2/trades/2026-10-05/2026-10-12/1"]);
        LedgerRequests.Select(r => r.Uri.Query).ShouldBe(["?from-date=2026-10-05&to-date=2026-10-12"]);
        TradeRequests.Concat(LedgerRequests).ShouldAllBe(r => r.Method == HttpMethod.Get && r.Header("access-token") == FakeBrokers.DhanAccessToken && r.Body == null);
        var lines = Factory.Logs.All.Skip(logged).ToList();
        lines.ShouldContain(line => line.Contains("Broker history", StringComparison.Ordinal)); // the run did log
        lines.ShouldAllBe(line => !line.Contains(AccountsApi.Marker, StringComparison.Ordinal) && !line.Contains(FakeBrokers.DhanAccessToken, StringComparison.Ordinal));
        lines.ShouldAllBe(line => !line.Contains("FUNDS DEPOSIT", StringComparison.Ordinal) && !line.Contains("T1", StringComparison.Ordinal) && !line.Contains("EXMPL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reading_again_stores_nothing_new_and_the_next_run_starts_at_the_watermark()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();
        await SyncAndWaitAsync(held.Account.Id);
        Dhan.ClearRequests();

        var (_, status) = await SyncAndWaitAsync(held.Account.Id, from: null);

        TradeRequests.Select(r => r.Uri.AbsolutePath).ShouldBe(["/v2/trades/2026-10-12/2026-10-12/0"]);
        LedgerRequests.Select(r => r.Uri.Query).ShouldBe(["?from-date=2026-10-12&to-date=2026-10-12"]);
        (status.Trades.LastRun!.Counts.Stored, status.Trades.LastRun.From).ShouldBe((0, D(12)));
        (await TransactionsAsync()).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM portfolio.ledger_entries")).ShouldBe(5);

        var again = await SyncAndWaitAsync(held.Account.Id);
        (again.Status.Trades.LastRun!.Counts.Stored, again.Status.Trades.LastRun.Counts.Duplicates).ShouldBe((0, 3));
        (again.Status.Ledger.LastRun!.Counts.Stored, again.Status.Ledger.LastRun.Counts.Duplicates).ShouldBe((0, 5));
    }

    [Fact]
    public async Task A_trade_already_imported_from_a_tradebook_file_is_a_duplicate_and_not_a_second_trade()
    {
        var held = await HeldAsync();
        await Client.ImportTradesOnlyAsync(held.Account.Id, Base, Ct);
        DhanHasTheBaseTrades();

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        (status.Trades.LastRun!.Counts.Stored, status.Trades.LastRun.Counts.Duplicates).ShouldBe((0, 3));
        (await TransactionsAsync()).ShouldBe(3);
        (await TransactionsAsync("source = 'csv'")).ShouldBe(3);
    }

    [Fact]
    public async Task A_voided_broker_trade_comes_back_when_its_day_is_read_again()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        await SyncAndWaitAsync(held.Account.Id);
        var trades = await Client.HoldingTradesAsync(held.HoldingId, "", Ct);
        using var voided = await Client.PostAsync($"/api/portfolio/trades/{trades.Items.Single(t => t.Side == "sell").Id}/void", Ct);
        voided.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        status.Trades.LastRun!.Counts.Stored.ShouldBe(1);
        (await TransactionsAsync("voided_at IS NULL")).ShouldBe(3);
        (await TransactionsAsync()).ShouldBe(4);
    }

    [Fact]
    public async Task The_first_run_without_a_start_date_reads_from_the_first_import_date()
    {
        var held = await HeldAsync();

        var (started, status) = await SyncAndWaitAsync(held.Account.Id, from: null);

        (started.Trades.From, started.Trades.To, started.Trades.Trigger).ShouldBe((D(9), D(12), "manual"));
        TradeRequests.Select(r => r.Uri.AbsolutePath).ShouldBe(["/v2/trades/2026-10-09/2026-10-12/0"]);
        (status.Trades.Watermark, status.Trades.WindowsTotal).ShouldBe((D(12), 1));
    }

    [Fact]
    public async Task A_long_period_is_read_in_windows_of_thirty_days_and_dhans_no_data_error_is_an_empty_window()
    {
        var held = await HeldAsync();
        Dhan.EmptyPageIsNoDataError = true;

        var (_, status) = await SyncAndWaitAsync(held.Account.Id, from: "2026-08-01");

        // Dhan's "no data" code on the first page of a window of several dates is unclear (it is also the answer to a request it does not like): each window is halved once, and a half that answers it again is empty - and counted.
        TradeRequests.Select(r => r.Uri.AbsolutePath).ShouldBe(
        [
            "/v2/trades/2026-08-01/2026-08-30/0", "/v2/trades/2026-08-01/2026-08-15/0", "/v2/trades/2026-08-16/2026-08-30/0",
            "/v2/trades/2026-08-31/2026-09-29/0", "/v2/trades/2026-08-31/2026-09-14/0", "/v2/trades/2026-09-15/2026-09-29/0",
            "/v2/trades/2026-09-30/2026-10-12/0", "/v2/trades/2026-09-30/2026-10-06/0", "/v2/trades/2026-10-07/2026-10-12/0",
        ]);
        (status.Trades.LastRun!.Status, status.Trades.WindowsDone, status.Trades.WindowsTotal, status.Trades.Watermark).ShouldBe(("succeeded", 3, 3, D(12)));
        (status.Trades.LastRun.Counts.Halved, status.Trades.LastRun.Counts.EmptyByNoData).ShouldBe((3, 6));
    }

    [Fact]
    public async Task A_start_date_that_would_leave_a_hole_after_the_watermark_is_refused_with_the_field_and_changes_nothing()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        Dhan.HistoryOverride = request => request.Uri.AbsolutePath.StartsWith("/v2/trades/2026-10-01/", StringComparison.Ordinal) ? FakeBrokers.Json(HttpStatusCode.InternalServerError, "{}") : null;
        var (_, failed) = await SyncAndWaitAsync(held.Account.Id, from: "2026-09-01");
        failed.Trades.Watermark.ShouldBe(new DateOnly(2026, 9, 30));
        Dhan.HistoryOverride = null;
        var requests = Dhan.Requests.Count;

        using var response = await Client.SyncNowRawAsync(held.Account.Id, "2026-10-05", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct))["from"].ShouldContain("Start on or before 2026-10-01: dates in between were never read.");
        Dhan.Requests.Count.ShouldBe(requests);
        (await Client.BrokerStatusAsync(held.Account.Id, Ct)).Trades.Watermark.ShouldBe(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public async Task A_window_that_fails_leaves_the_watermark_where_the_last_whole_window_put_it_and_the_next_run_reads_it_again()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T0", "EXMPL", "BUY", 1, 80m, new DateOnly(2026, 9, 15), Morning));
        Dhan.Trades.Add(new FakeBrokers.FakeTrade("T9", "EXMPL", "BUY", 1, 140m, D(12), Morning));
        Dhan.HistoryOverride = request => request.Uri.AbsolutePath.StartsWith("/v2/trades/2026-10-01/", StringComparison.Ordinal) ? FakeBrokers.Json(HttpStatusCode.InternalServerError, "{}") : null;

        var (_, failed) = await SyncAndWaitAsync(held.Account.Id, from: "2026-09-01");

        (failed.Trades.LastRun!.Status, failed.Trades.WindowsDone, failed.Trades.WindowsTotal, failed.Trades.Watermark).ShouldBe(("failed", 1, 2, new DateOnly(2026, 9, 30)));
        failed.Trades.LastRun.Error.ShouldNotBeNull().ShouldContain("could not be reached");
        failed.Ledger.LastRun.ShouldBeNull();
        (await TransactionsAsync()).ShouldBe(1);
        (await LotsOfAsync(held.HoldingId)).Lots.ShouldHaveSingleItem(); // not rebuilt from a history with a window missing

        Dhan.HistoryOverride = null;
        var (_, next) = await SyncAndWaitAsync(held.Account.Id, from: null);

        (next.Trades.LastRun!.Status, next.Trades.LastRun.From, next.Trades.LastRun.Counts.Stored, next.Trades.Watermark).ShouldBe(("succeeded", new DateOnly(2026, 9, 30), 4, D(12)));
        (await TransactionsAsync()).ShouldBe(5);
        next.HoldingsRebuilt.ShouldBe(1);
    }

    [Fact]
    public async Task A_token_that_dhan_refuses_fails_the_run_with_a_safe_text_and_stores_nothing()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        Dhan.HistoryOverride = _ => FakeBrokers.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"token {{FakeBrokers.DhanAccessToken}} is invalid"}""");

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        var run = status.Trades.LastRun!;
        (run.Status, run.Error).ShouldBe(("failed", "Dhan refused the saved access token. Sign in to Dhan again (Accounts, Connection) and retry."));
        (status.Trades.Watermark, status.Ledger.LastRun).ShouldBe((null, null));
        (await TransactionsAsync()).ShouldBe(0);
        System.Text.Json.JsonSerializer.Serialize(status).ShouldNotContain(FakeBrokers.DhanAccessToken);
    }

    [Fact]
    public async Task The_runs_are_audited_with_counts_only()
    {
        var held = await HeldAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();

        await SyncAndWaitAsync(held.Account.Id);

        (await EventsAsync("BrokerHistorySynced")).ShouldBe(2);
        var details = await Factory.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.AccountEvents.Where(e => e.Event.ToString() == "BrokerHistorySynced" || e.Event.ToString() == "LotsRebuilt").Select(e => e.Detail!), Ct));
        details.ShouldContain("Broker trades read (manual): 1 windows, 3 new trades, 0 already stored, 0 skipped, 0 unusable; 1 holdings rebuilt, 0 need review");
        details.ShouldContain("Broker ledger read (manual): 1 windows, 5 new entries, 0 already stored, 0 unusable");
        details.ShouldContain(d => d.EndsWith("trigger broker_sync", StringComparison.Ordinal));
        details.ShouldAllBe(d => !d.Contains("EXMPL", StringComparison.Ordinal) && !d.Contains("130", StringComparison.Ordinal) && !d.Contains("FUNDS", StringComparison.Ordinal) && !d.Contains(FakeBrokers.DhanAccessToken, StringComparison.Ordinal));
    }

    // ---- the ledger list ----

    [Fact]
    public async Task The_ledger_is_listed_newest_first_with_classes_totals_and_filters()
    {
        var held = await HeldAsync();
        DhanHasALedger();
        await SyncAndWaitAsync(held.Account.Id);

        var all = await Client.LedgerAsync(held.Account.Id, "", Ct);
        var dividends = await Client.LedgerAsync(held.Account.Id, "?class=dividend", Ct);
        var period = await Client.LedgerAsync(held.Account.Id, "?from=2026-10-05&to=2026-10-07&pageSize=2&page=2", Ct);

        all.Items.Select(i => (i.VoucherNo, i.Class, i.Amount)).ShouldBe(
        [
            ("V5", "funds_out", -10000m),
            ("V4", "charges", -15.93m),
            ("V3", "dividend", 1500m),
            ("V2", "other", -900m),
            ("V1", "funds_in", 25000m),
        ],
        ignoreOrder: true);
        all.Items.Select(i => i.VoucherNo).Take(3).ShouldBe(["V5", "V4", "V3"]);
        (all.Page, all.PageSize, all.Total).ShouldBe((1, 50, 5));
        all.Totals.Select(t => (t.Class, t.Count, t.Amount)).ShouldBe([("funds_in", 1, 25000m), ("funds_out", 1, -10000m), ("dividend", 1, 1500m), ("charges", 1, -15.93m), ("other", 1, -900m)]);
        var entry = all.Items.Single(i => i.VoucherNo == "V1");
        (entry.Date, entry.Description, entry.Narration, entry.Exchange, entry.RunningBalance).ShouldBe((D(5), "RECEIPT", "FUNDS DEPOSIT THROUGH UPI", "NSE-CAPITAL", 25000m));
        dividends.Items.Select(i => i.VoucherNo).ShouldBe(["V3"]);
        dividends.Total.ShouldBe(1);
        dividends.Totals.Sum(t => t.Count).ShouldBe(5); // the totals follow the dates, not the class
        (period.Page, period.PageSize, period.Total, period.Items.Count).ShouldBe((2, 2, 3, 1));
        period.Totals.Sum(t => t.Count).ShouldBe(3);
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=201", "pageSize")]
    [InlineData("?class=gifts", "class")]
    [InlineData("?from=2026-10-09&to=2026-10-01", "from")]
    public async Task A_bad_ledger_query_is_refused_with_the_field(string query, string field)
    {
        var held = await HeldAsync();

        using var response = await Client.GetAsync($"/api/accounts/{held.Account.Id}/ledger{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain(field);
    }

    [Fact]
    public async Task The_ledger_and_status_of_an_unknown_account_are_not_found_and_an_account_never_read_has_empty_ones()
    {
        var held = await HeldAsync();

        using var ledger = await Client.GetAsync($"/api/accounts/{Guid.NewGuid()}/ledger", Ct);
        using var status = await Client.GetAsync($"/api/accounts/{Guid.NewGuid()}/sync/broker-history/status", Ct);
        var empty = await Client.LedgerAsync(held.Account.Id, "", Ct);
        var idle = await Client.BrokerStatusAsync(held.Account.Id, Ct);

        (ledger.StatusCode, status.StatusCode).ShouldBe((HttpStatusCode.NotFound, HttpStatusCode.NotFound));
        empty.Items.ShouldBeEmpty();
        empty.Total.ShouldBe(0);
        empty.Totals.Select(t => t.Count).ShouldBe([0, 0, 0, 0, 0]);
        (idle.Running, idle.Trades.Watermark, idle.Trades.LastRun, idle.Ledger.LastRun, idle.HoldingsRebuilt).ShouldBe((false, null, null, null, 0));
    }
}
