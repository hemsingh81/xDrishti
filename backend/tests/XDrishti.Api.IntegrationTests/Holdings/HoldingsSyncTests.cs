using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>"Sync now" over HTTP on the real database: every rule of ADR 0012, rollback, idempotence and the one-run-at-a-time lock (clock: Monday 2026-10-12 10:00 India time).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsSyncTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private static readonly DateOnly Imported = new(2025, 1, 1);

    private static readonly DateOnly Today = new(2026, 10, 12);

    private async Task<AccountDetail> ImportedAccountAsync()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("GAMMA", 30, 40m, 41m, "1003", "INE000G01013"),
        ]);
        await ImportAllAsync(account.Id, "2025-01-01");
        return account;
    }

    [Fact]
    public async Task A_new_holding_gets_a_lot_dated_today_and_the_run_is_recorded()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings.Add(Holding("DELTA", 100, 50m, 60m, "1004", "INE000D01014"));
        var before = HoldingRequests.Count;

        var run = await Client.SyncAsync(account.Id, Ct);

        (run.Trigger, run.Status, run.NewCount, run.UnchangedCount, run.Error).ShouldBe(("manual", "succeeded", 1, 3, null));
        var lot = (await Factory.HoldingAsync("DELTA", Ct)).Lots.Single();
        (lot.AcquiredOn, lot.Source, lot.UsesDefaultDate, lot.Qty, lot.UnitCost).ShouldBe((Today, LotSource.Added, false, 100L, 50m));
        HoldingRequests.Count.ShouldBe(before + 1);
        HoldingRequests.Last().Header("access-token").ShouldBe(FakeBrokers.DhanAccessToken);
    }

    [Fact]
    public async Task Increase_decrease_missing_and_unchanged_are_handled_in_one_sync()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings.Clear();
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 130, 53m, 70m, "1001", "INE000A01011"), // +30: implied cost 63
            Holding("BETA", 4, 200m, 170m, "1002", "INE000B01012"), // -6 at 170
            // GAMMA is gone
            Holding("DELTA", 8, 25m, 26m, "1004", "INE000D01014"), // new
        ]);

        var run = await Client.SyncAsync(account.Id, Ct);

        (run.NewCount, run.IncreasedCount, run.DecreasedCount, run.ClosedCount, run.UnchangedCount).ShouldBe((1, 1, 1, 1, 0));
        var alpha = await Factory.HoldingAsync("ALPHA", Ct);
        var added = alpha.Lots.Single(l => l.Source == LotSource.Added);
        (added.AcquiredOn, added.Qty, added.UnitCost).ShouldBe((Today, 30L, 63m));
        (alpha.Qty, alpha.AvgCost, alpha.LastPrice).ShouldBe((130L, 53m, 70m));
        var beta = await Factory.HoldingAsync("BETA", Ct);
        var closedPiece = beta.Lots.Single(l => !l.IsOpen);
        (closedPiece.Qty, closedPiece.ClosePrice, closedPiece.ClosedOn, closedPiece.Estimated).ShouldBe((6L, 170m, Today, true));
        beta.Lots.Single(l => l.IsOpen).Qty.ShouldBe(4L);
        var gamma = await Factory.HoldingAsync("GAMMA", Ct);
        (gamma.Status, gamma.Qty, gamma.ClosedAt.HasValue).ShouldBe((HoldingStatus.Closed, 0L, true));
        gamma.Lots.Single().ClosePrice.ShouldBe(41m);
        (await Factory.HoldingAsync("DELTA", Ct)).Lots.Single().AcquiredOn.ShouldBe(Today);
    }

    [Fact]
    public async Task A_second_identical_sync_changes_nothing_but_prices()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings[0] = Holding("ALPHA", 130, 53m, 70m, "1001", "INE000A01011");
        await Client.SyncAsync(account.Id, Ct);
        var lotsBefore = await Factory.WithDbAsync(db => db.HoldingLots.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Qty, l.AcquiredOn, l.ClosedOn }).ToListAsync(Ct));
        Dhan.Holdings[1] = Holding("BETA", 10, 200m, 150m, "1002", "INE000B01012"); // only a price moved

        var second = await Client.SyncAsync(account.Id, Ct);

        (second.NewCount, second.IncreasedCount, second.DecreasedCount, second.ClosedCount, second.UnchangedCount).ShouldBe((0, 0, 0, 0, 3));
        var lotsAfter = await Factory.WithDbAsync(db => db.HoldingLots.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Qty, l.AcquiredOn, l.ClosedOn }).ToListAsync(Ct));
        lotsAfter.ShouldBe(lotsBefore);
        (await Factory.HoldingAsync("BETA", Ct)).LastPrice.ShouldBe(150m);
    }

    [Fact]
    public async Task A_holding_that_returns_after_being_closed_is_reopened_with_a_lot_dated_today()
    {
        var account = await ImportedAccountAsync();
        var removed = Dhan.Holdings[2];
        Dhan.Holdings.RemoveAt(2);
        await Client.SyncAsync(account.Id, Ct);
        Dhan.Holdings.Add(removed with { Qty = 12 });

        var run = await Client.SyncAsync(account.Id, Ct);

        run.NewCount.ShouldBe(1);
        var gamma = await Factory.HoldingAsync("GAMMA", Ct);
        (gamma.Status, gamma.Qty, gamma.ClosedAt).ShouldBe((HoldingStatus.Open, 12L, (DateTimeOffset?)null));
        gamma.Lots.Count.ShouldBe(2);
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(3);
    }

    [Fact]
    public async Task Holdings_are_mapped_by_isin_then_by_security_id_and_unmapped_ones_are_kept_with_returns()
    {
        var account = await ConnectedAccountAsync();
        var alpha = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "9001", "Alpha Industries", Ct);
        var beta = await Factory.SeedInstrumentAsync("BETA", "INE000B99999", "1002", "Beta Limited", Ct);
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"), // by ISIN (security id differs)
            Holding("BETA", 10, 200m, 180m, "1002", null), // by security id
            Holding("BSEONLY", 5, 10m, 12m, "500999", "INE000Z01099", "BSE"),
        ]);

        await Client.SyncAsync(account.Id, Ct);

        (await Factory.HoldingAsync("ALPHA", Ct)).InstrumentId.ShouldBe(alpha);
        (await Factory.HoldingAsync("BETA", Ct)).InstrumentId.ShouldBe(beta);
        var unmapped = await Factory.HoldingAsync("BSEONLY", Ct);
        unmapped.InstrumentId.ShouldBeNull();
        var rows = (await Client.ListHoldingsAsync("?sort=symbol", Ct)).Items;
        rows.Select(r => (r.Symbol, r.Mapped)).ShouldBe([("ALPHA", true), ("BETA", true), ("BSEONLY", false)]);
        rows[2].Returns.Invested.ShouldBe(50m);
        rows[2].Returns.Value.ShouldBe(60m);
        (await Client.SummaryAsync("", Ct)).UnmappedCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_after_the_changes_were_saved_rolls_everything_back_and_records_the_failed_run()
    {
        var account = await ImportedAccountAsync();
        var before = await Factory.WithDbAsync(db => db.HoldingLots.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Qty, l.ClosedOn }).ToListAsync(Ct));
        var holdingsBefore = await Factory.WithDbAsync(db => db.Holdings.AsNoTracking().OrderBy(h => h.Id).Select(h => new { h.Id, h.Qty, h.LastPrice, h.Status }).ToListAsync(Ct));
        Dhan.Holdings[0] = Holding("ALPHA", 130, 53m, 70m, "1001", "INE000A01011");
        Dhan.Holdings.RemoveAt(2);
        Dhan.Holdings.Add(Holding("DELTA", 8, 25m, 26m, "1004", "INE000D01014"));
        // Ending a run as succeeded is the last step of the transaction: make that step fail, after the holdings and audit entry were saved.
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE OR REPLACE FUNCTION portfolio.test_fail_success() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'simulated failure while ending the run';
                END $$ LANGUAGE plpgsql;
                CREATE TRIGGER test_fail_success BEFORE UPDATE ON portfolio.holding_syncs
                    FOR EACH ROW WHEN (NEW.status = 'succeeded') EXECUTE FUNCTION portfolio.test_fail_success();
                """,
                Ct);
            return 0;
        });
        try
        {
            using var response = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.sync_failed");
        }
        finally
        {
            await Factory.WithDbAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS test_fail_success ON portfolio.holding_syncs; DROP FUNCTION IF EXISTS portfolio.test_fail_success();", Ct);
                return 0;
            });
        }

        (await Factory.WithDbAsync(db => db.HoldingLots.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Qty, l.ClosedOn }).ToListAsync(Ct))).ShouldBe(before);
        (await Factory.WithDbAsync(db => db.Holdings.AsNoTracking().OrderBy(h => h.Id).Select(h => new { h.Id, h.Qty, h.LastPrice, h.Status }).ToListAsync(Ct))).ShouldBe(holdingsBefore);
        var runs = await Factory.SyncsAsync(account.Id, Ct);
        var failed = runs.Last();
        (failed.Trigger, failed.Status).ShouldBe((SyncTrigger.Manual, SyncStatus.Failed));
        failed.Error.ShouldBe("The sync failed unexpectedly; nothing was changed. See the API log.");
        failed.Error.ShouldNotBeNull().ShouldNotContain("simulated");
        var activity = await (await Client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity.ShouldNotContain(e => e.Event == "HoldingsSynced");
        // The next sync works again and applies the changes.
        (await Client.SyncAsync(account.Id, Ct)).Status.ShouldBe("succeeded");
        (await Factory.HoldingAsync("DELTA", Ct)).Qty.ShouldBe(8L);
    }

    [Fact]
    public async Task A_second_sync_while_one_runs_is_a_409_and_the_database_allows_one_running_row_per_account()
    {
        var account = await ImportedAccountAsync();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dhan.BeforeHoldingsAnswer = async () =>
        {
            arrived.TrySetResult();
            await release.Task;
        };
        var first = Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);
        await arrived.Task.WaitAsync(Ct);

        using var second = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);
        var status = await Client.SyncStatusAsync(account.Id, Ct);
        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => Factory.WithDbAsync(async db =>
        {
            db.HoldingSyncs.Add(HoldingSync.Start(account.Id, SyncTrigger.Manual, "x", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
            return 0;
        }));
        release.SetResult();
        using var firstResponse = await first;

        (second.StatusCode, (await second.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.sync_running"));
        status.Running.ShouldBeTrue();
        ((PostgresException)duplicate.InnerException!).ConstraintName.ShouldBe("ux_holding_syncs_one_running");
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.SyncStatusAsync(account.Id, Ct)).Running.ShouldBeFalse();
        HoldingRequests.Count.ShouldBe(2); // the import's fetch and the first sync's; the refused one never reached Dhan
    }

    [Fact]
    public async Task Two_syncs_started_at_the_same_moment_run_one_after_the_other_never_together()
    {
        var account = await ImportedAccountAsync();
        var active = 0;
        var maxActive = 0;
        Dhan.BeforeHoldingsAnswer = async () =>
        {
            var now = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, now);
            await Task.Delay(150, Ct);
            Interlocked.Decrement(ref active);
        };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct)));

        maxActive.ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_run_that_was_left_running_long_ago_does_not_block_the_next_sync()
    {
        var account = await ImportedAccountAsync();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO portfolio.holding_syncs (id, account_id, \"trigger\", requested_by, started_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count) "
                + "VALUES (gen_random_uuid(), {0}, 'manual', 'x', {1}, 'running', 0, 0, 0, 0, 0)",
                account.Id,
                Factory.Clock.GetUtcNow().AddMinutes(-30));
            return 0;
        });

        (await Client.SyncAsync(account.Id, Ct)).Status.ShouldBe("succeeded");

        var stale = (await Factory.SyncsAsync(account.Id, Ct)).Single(s => s.RequestedBy == "x");
        (stale.Status, stale.Error).ShouldBe((SyncStatus.Failed, "The sync did not finish (the application stopped or crashed while it ran)."));
    }

    [Fact]
    public async Task The_sync_is_audited_with_counts_only_and_no_secret_reaches_responses_audit_or_logs()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings[0] = Holding("ALPHA", 130, 53m, 70m, "1001", "INE000A01011");

        using var response = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var activityBody = await (await Client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).Content.ReadAsStringAsync(Ct);
        var statusBody = await (await Client.GetAsync($"/api/accounts/{account.Id}/holdings/sync-status", Ct)).Content.ReadAsStringAsync(Ct);

        activityBody.ShouldContain("Holdings synced (manual): 0 new, 1 increased, 0 decreased, 0 closed, 2 unchanged");
        foreach (var text in new[] { body, activityBody, statusBody })
        {
            AssertNoSecret(text);
        }

        Factory.Logs.All.ShouldAllBe(line => !line.Contains(AccountsApi.Marker, StringComparison.Ordinal) && !line.Contains(FakeBrokers.DhanAccessToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_needs_the_connection_and_the_portfolio_role()
    {
        var offline = await UnconnectedAccountAsync();
        var noRole = await UnconnectedAccountAsync("Example Two", "1000000002", portfolioRole: false);

        using var notConnected = await Client.PostAsync($"/api/accounts/{offline.Id}/holdings/sync", Ct);
        using var roleOff = await Client.PostAsync($"/api/accounts/{noRole.Id}/holdings/sync", Ct);
        using var unknown = await Client.PostAsync($"/api/accounts/{Guid.NewGuid()}/holdings/sync", Ct);

        (notConnected.StatusCode, (await notConnected.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.not_connected"));
        (roleOff.StatusCode, (await roleOff.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.role_off"));
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        HoldingRequests.ShouldBeEmpty();
        (await Factory.CountAsync("portfolio.holding_syncs", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_token_dhan_refuses_is_a_409_asking_to_sign_in_again_and_a_failed_run()
    {
        var account = await ImportedAccountAsync();
        Dhan.Override("/v2/holdings", _ => FakeBrokers.Json(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901"}"""));

        using var response = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.not_connected");
        var status = await Client.SyncStatusAsync(account.Id, Ct);
        (status.Last!.Status, status.Last.Trigger).ShouldBe(("failed", "manual"));
        status.Last.Error.ShouldNotBeNull().ShouldContain("Sign in to Dhan again");
        (await Factory.HoldingAsync("ALPHA", Ct)).Qty.ShouldBe(100L);
    }

    [Fact]
    public async Task A_dhan_outage_is_a_502_and_a_failed_run_that_changes_nothing()
    {
        var account = await ImportedAccountAsync();
        Dhan.Override("/v2/holdings", _ => FakeBrokers.Json(HttpStatusCode.ServiceUnavailable, """{"errorCode":"DH-908"}"""));

        using var response = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.broker_error");
        (await Factory.SyncsAsync(account.Id, Ct)).Last().Status.ShouldBe(SyncStatus.Failed);
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(3);
    }

    [Fact]
    public async Task An_empty_answer_is_refused_with_the_counts_until_it_is_confirmed_and_then_closes_everything()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings.Clear();

        using var refused = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);
        var problem = System.Text.Json.JsonDocument.Parse(await refused.Content.ReadAsStringAsync(Ct)).RootElement;

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("portfolio.confirmation_required");
        (problem.GetProperty("listedHoldings").GetInt32(), problem.GetProperty("openHoldings").GetInt32(), problem.GetProperty("wouldClose").GetInt32()).ShouldBe((0, 3, 3));
        problem.GetProperty("title").GetString().ShouldBe("Dhan listed 0 holdings while 3 are open in xDrishti — nothing was changed; press Sync now to confirm.");
        (await Client.SummaryAsync($"?accountId={account.Id}", Ct)).OpenCount.ShouldBe(3);
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(Imported);
        (await Factory.SyncsAsync(account.Id, Ct)).Count.ShouldBe(1); // only the import

        using var confirmed = await Client.PostJsonAsync($"/api/accounts/{account.Id}/holdings/sync", new { confirm = true, expectedOpenHoldings = 3, expectedWouldClose = 3 }, Ct);
        var run = await confirmed.ReadAsync<SyncRunView>(Ct);

        run.ClosedCount.ShouldBe(3);
        (await Client.SummaryAsync($"?accountId={account.Id}", Ct)).ClosedCount.ShouldBe(3);
    }

    [Fact]
    public async Task Dhans_no_data_error_is_an_empty_answer_and_so_needs_the_same_confirmation()
    {
        var account = await ImportedAccountAsync();
        Dhan.Override("/v2/holdings", _ => FakeBrokers.Json(HttpStatusCode.BadRequest, """{"errorType":"Data_API_Error","errorCode":"DH-907","errorMessage":"no data"}"""));

        using var refused = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);

        (refused.StatusCode, (await refused.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.confirmation_required"));
        (await Factory.HoldingAsync("GAMMA", Ct)).Status.ShouldBe(HoldingStatus.Open);
    }

    [Fact]
    public async Task More_than_half_missing_needs_confirmation_but_exactly_half_does_not()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange(Enumerable.Range(1, 6).Select(i => Holding($"S{i:00}", 10, 100m, 110m, $"{1000 + i}", $"INE{i:000}A01011")));
        await ImportAllAsync(account.Id, "2025-01-01");
        Dhan.Holdings.RemoveRange(2, 4);

        using var refused = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);
        Dhan.Holdings.AddRange(Enumerable.Range(3, 2).Select(i => Holding($"S{i:00}", 10, 100m, 110m, $"{1000 + i}", $"INE{i:000}A01011"))); // 4 of 6 listed again
        using var half = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        half.StatusCode.ShouldBe(HttpStatusCode.OK); // 4 listed, 2 of 6 missing
        (await half.ReadAsync<SyncRunView>(Ct)).ClosedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Confirming_without_the_numbers_is_a_400_and_with_other_numbers_than_dhan_gives_now_a_409_with_the_fresh_ones()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings.Clear();

        using var noNumbers = await Client.PostJsonAsync($"/api/accounts/{account.Id}/holdings/sync", new { confirm = true }, Ct);
        using var stale = await Client.PostJsonAsync($"/api/accounts/{account.Id}/holdings/sync", new { confirm = true, expectedOpenHoldings = 3, expectedWouldClose = 2 }, Ct);
        var problem = System.Text.Json.JsonDocument.Parse(await stale.Content.ReadAsStringAsync(Ct)).RootElement;

        noNumbers.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noNumbers.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid");
        (await noNumbers.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["expectedOpenHoldings", "expectedWouldClose"], ignoreOrder: true);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("portfolio.confirmation_stale");
        (problem.GetProperty("listedHoldings").GetInt32(), problem.GetProperty("openHoldings").GetInt32(), problem.GetProperty("wouldClose").GetInt32()).ShouldBe((0, 3, 3));
        (await Client.SummaryAsync($"?accountId={account.Id}", Ct)).OpenCount.ShouldBe(3);
        (await Factory.SyncsAsync(account.Id, Ct)).Count.ShouldBe(1); // only the import: neither refusal left a record
    }

    [Fact]
    public async Task Closing_a_lot_is_not_possible_before_its_purchase_and_a_closed_lots_date_cannot_pass_its_closing_day()
    {
        var account = await ImportedAccountAsync();
        Dhan.Holdings[1] = Holding("BETA", 4, 200m, 170m, "1002", "INE000B01012"); // 6 sold today (2026-10-12)
        await Client.SyncAsync(account.Id, Ct);
        var beta = (await Client.ListHoldingsAsync("?q=BETA", Ct)).Items.Single();
        var closed = (await Client.LotsAsync(beta.Id, Ct)).Lots.Single(l => l.Status == "closed");

        using var tooLate = await Client.PutJsonAsync($"/api/holdings/{beta.Id}/lots/{closed.Id}", new { acquiredOn = "2026-10-13" }, Ct);
        using var onTheDay = await Client.PutJsonAsync($"/api/holdings/{beta.Id}/lots/{closed.Id}", new { acquiredOn = "2026-10-12" }, Ct);

        tooLate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLate.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid");
        onTheDay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var violation = await Should.ThrowAsync<PostgresException>(() => Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE portfolio.holding_lots SET acquired_on = '2026-10-20' WHERE closed_on IS NOT NULL", Ct);
            return 0;
        }));
        violation.ConstraintName.ShouldBe("ck_holding_lots_closed_after_acquired"); // the database guards it too, against a concurrent close
    }
}
