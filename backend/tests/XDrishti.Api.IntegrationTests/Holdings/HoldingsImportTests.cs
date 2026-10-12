using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>Preview and the first import (saving the Portfolio settings, ADR 0013) over HTTP against a fake Dhan and the real database (clock: Monday 2026-10-12 10:00 India time).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsImportTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private static readonly DateOnly DefaultDate = new(2025, 1, 1);

    private static readonly DateOnly Today = new(2026, 10, 12);

    private void DhanHolds()
    {
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("GAMMA", 30, 40m, 41m, "1003", null), // no ISIN: found by security id
            Holding("BSEONLY", 5, 10m, 12m, "500999", "INE000Z01099", "BSE"), // not in the master
        ]);
    }

    [Fact]
    public async Task The_preview_asks_dhan_once_with_the_stored_token_maps_instruments_and_saves_nothing()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();
        var alpha = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        var gamma = await Factory.SeedInstrumentAsync("GAMMA", "INE000G01017", "1003", "Gamma Limited", Ct); // the row has no ISIN here: matched by security id

        var preview = await Client.PreviewAsync(account.Id, Ct);

        var request = HoldingRequests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/holdings");
        request.Header("access-token").ShouldBe(FakeBrokers.DhanAccessToken);
        preview.DefaultDate.ShouldBe(DefaultDate);
        preview.Rows.Select(r => (r.Key, r.Symbol, r.Name, r.Mapped, r.AlreadyImported)).ShouldBe(
        [
            ("INE000A01011", "ALPHA", "Alpha Industries", true, false),
            ("INE000B01012", "BETA", "BETA", false, false),
            ("1003:ALL", "GAMMA", "Gamma Limited", true, false),
            ("INE000Z01099", "BSEONLY", "BSEONLY", false, false),
        ]);
        preview.Rows[0].InstrumentId.ShouldBe(alpha);
        preview.Rows[2].InstrumentId.ShouldBe(gamma);
        preview.Rows[0].CanonicalSymbol.ShouldBe("ALPHA");
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(0);
        (await Factory.CountAsync("portfolio.holding_syncs", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Mapping_prefers_the_isin_over_the_security_id()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 1, 1m, 1m, "1001", "INE000A01011"));
        var byIsin = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "7777", "Right One", Ct);
        await Factory.SeedInstrumentAsync("OTHER", "INE000X01010", "1001", "Wrong One", Ct); // same security id, other ISIN

        var preview = await Client.PreviewAsync(account.Id, Ct);

        preview.Rows.Single().InstrumentId.ShouldBe(byIsin);
        preview.Rows.Single().Name.ShouldBe("Right One");
    }

    [Fact]
    public async Task Saving_the_settings_imports_holdings_with_lots_dated_by_the_owner_and_flags_the_default_date_ones()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();
        await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);

        var saved = await Client.SavePortfolioAsync(account.Id, new
        {
            refreshMode = "daily",
            refreshDailyTimeIst = "17:00",
            firstImportDate = "2025-01-01",
            firstImport = new
            {
                rows = new object[]
                {
                    new { key = "INE000A01011", acquiredOn = "2023-05-05" },
                    new { key = "INE000B01012" },
                    new { key = "1003:ALL" },
                    new { key = "INE000Z01099", acquiredOn = "2026-10-12" },
                    new { key = "NOPE" },
                },
            },
        }, Ct);

        var import = saved.Import!;
        (import.Status, import.Imported, import.DefaultDated, import.AlreadyImported, import.Unknown).ShouldBe(("imported", 4, 2, 0, 0)); // every holding Dhan lists; the rows only override dates, an unknown key is ignored
        (saved.Account.Portfolio.RefreshMode, saved.Account.Portfolio.RefreshDailyTimeIst, saved.Account.Portfolio.FirstImportDate, saved.Account.Portfolio.HasHoldings)
            .ShouldBe(("daily", "17:00", DefaultDate, true));
        var alpha = await Factory.HoldingAsync("ALPHA", Ct);
        (alpha.Qty, alpha.AvgCost, alpha.LastPrice, alpha.Status, alpha.IsMapped, alpha.Name).ShouldBe((100L, 50m, 60m, HoldingStatus.Open, true, "Alpha Industries"));
        var lot = alpha.Lots.Single();
        (lot.AcquiredOn, lot.Qty, lot.UnitCost, lot.Source, lot.UsesDefaultDate).ShouldBe((new DateOnly(2023, 5, 5), 100L, 50m, LotSource.Import, false));
        var beta = (await Factory.HoldingAsync("BETA", Ct)).Lots.Single();
        (beta.AcquiredOn, beta.UsesDefaultDate).ShouldBe((DefaultDate, true));
        (await Factory.HoldingAsync("BSEONLY", Ct)).IsMapped.ShouldBeFalse();
        var run = (await Factory.SyncsAsync(account.Id, Ct)).Single();
        (run.Trigger, run.Status, run.NewCount).ShouldBe((SyncTrigger.Import, SyncStatus.Succeeded, 4));
        var bucket = await Factory.WithDbAsync(db => db.PortfolioBuckets.AsNoTracking().SingleAsync(b => b.DefaultForAccountId == account.Id, Ct));
        (await Factory.HoldingAsync("ALPHA", Ct)).BucketId.ShouldBe(bucket.Id);
    }

    [Fact]
    public async Task The_import_goes_to_the_custom_bucket_chosen_in_the_same_save()
    {
        var account = await ConnectedAccountAsync();
        using var added = await Client.PostJsonAsync("/api/portfolio-buckets", new { name = "Growth" }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        Dhan.Holdings.Add(Holding("ALPHA", 1, 1m, 1m, "1001", "INE000A01011"));

        await Client.SavePortfolioAsync(account.Id, new { defaultBucket = "Growth", firstImport = new { rows = new[] { new { key = "INE000A01011" } } } }, Ct);

        var bucket = await Factory.WithDbAsync(db => db.PortfolioBuckets.AsNoTracking().SingleAsync(b => b.Name == "Growth", Ct));
        (await Factory.HoldingAsync("ALPHA", Ct)).BucketId.ShouldBe(bucket.Id);
    }

    [Fact]
    public async Task A_save_without_rows_imports_every_holding_with_the_stored_date_and_a_second_save_imports_nothing_more()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();

        var first = (await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2024-02-02" }, Ct)).Import!;
        var requests = HoldingRequests.Count;
        var second = (await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2024-03-03" }, Ct)).Import!;
        var third = (await Client.SavePortfolioAsync(account.Id, new { }, Ct)).Import!;

        (first.Status, first.Imported, first.DefaultDated).ShouldBe(("imported", 4, 4));
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().ShouldSatisfyAllConditions(
            l => l.AcquiredOn.ShouldBe(new DateOnly(2024, 3, 3)), // the second save changed the date: the default-dated lot followed (ADR 0015)
            l => l.UsesDefaultDate.ShouldBeTrue());
        (second.Status, second.Imported, second.Redated).ShouldBe(("skipped", 0, 4));
        third.Status.ShouldBe("skipped");
        HoldingRequests.Count.ShouldBe(requests); // an account with holdings is not asked Dhan again
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(4);
    }

    [Fact]
    public async Task An_account_that_is_not_connected_gets_a_deferred_import_and_the_date_is_stored()
    {
        var account = await UnconnectedAccountAsync();

        var saved = await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2024-02-02" }, Ct);

        saved.Import!.Status.ShouldBe("deferred");
        saved.Account.Portfolio.FirstImportDate.ShouldBe(new DateOnly(2024, 2, 2));
        HoldingRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_proposes_the_stored_first_import_date()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();
        await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2024-02-02" }, Ct);

        var preview = await Client.PreviewAsync(account.Id, Ct);

        preview.DefaultDate.ShouldBe(new DateOnly(2024, 2, 2));
        preview.Rows.ShouldAllBe(r => r.SuggestedDate == new DateOnly(2024, 2, 2));
    }

    [Fact]
    public async Task Dates_before_1990_or_after_today_and_blank_keys_are_rejected_and_nothing_is_saved()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();

        using var response = await Client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new
        {
            refreshMode = "daily",
            firstImportDate = "2026-10-13",
            firstImport = new
            {
                rows = new object[] { new { key = "INE000A01011", acquiredOn = "2026-10-13" }, new { key = "" }, new { key = "INE000B01012", acquiredOn = "1989-12-31" } },
            },
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.invalid");
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(
            ["firstImportDate", "firstImport.rows[0].acquiredOn", "firstImport.rows[1].key", "firstImport.rows[2].acquiredOn"], ignoreOrder: true);
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(0);
        (await Client.GetAccountAsync(account.Id, Ct)).Portfolio.RefreshMode.ShouldBe("off");
        HoldingRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Today_and_1990_01_01_are_valid_dates()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();

        await Client.ImportAsync(account.Id, "2026-10-12", new[] { new { key = "INE000A01011", acquiredOn = "1990-01-01" }, new { key = "INE000B01012", acquiredOn = "2026-10-12" } }, Ct);

        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(new DateOnly(1990, 1, 1));
        (await Factory.HoldingAsync("BETA", Ct)).Lots.Single().AcquiredOn.ShouldBe(Today);
    }

    [Fact]
    public async Task The_import_is_audited_with_counts_only()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();

        await Client.ImportAsync(account.Id, "2025-01-01", new object[] { new { key = "INE000A01011" }, new { key = "NOPE" } }, Ct); // all four are imported

        var activity = await (await Client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity[0].Event.ShouldBe("HoldingsImported");
        activity[0].Detail.ShouldBe("Holdings imported: 4 imported, 0 already imported, 0 not listed by Dhan; 4 lots on the default date 2025-01-01");
        AssertNoSecret(activity[0].Detail!);
    }

    [Fact]
    public async Task An_account_that_is_not_connected_or_has_no_portfolio_role_or_does_not_exist_is_refused_by_the_preview()
    {
        var offline = await UnconnectedAccountAsync();
        var noRole = await UnconnectedAccountAsync("Example Two", "1000000002", portfolioRole: false);

        using var notConnected = await Client.PostAsync($"/api/accounts/{offline.Id}/holdings/preview", Ct);
        using var roleOff = await Client.PostAsync($"/api/accounts/{noRole.Id}/holdings/preview", Ct);
        using var unknown = await Client.PostAsync($"/api/accounts/{Guid.NewGuid()}/holdings/preview", Ct);

        (notConnected.StatusCode, (await notConnected.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.not_connected"));
        (roleOff.StatusCode, (await roleOff.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.role_off"));
        (unknown.StatusCode, (await unknown.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.NotFound, "accounts.not_found"));
        HoldingRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_for_an_account_that_is_not_connected_stores_the_date_and_the_first_sync_imports_everything_with_it()
    {
        var account = await UnconnectedAccountAsync();
        DhanHolds();

        var saved = await Client.SavePortfolioAsync(account.Id, new { firstImportDate = "2024-03-03", firstImport = new { rows = new[] { new { key = "INE000A01011" } } } }, Ct);

        saved.Import!.Status.ShouldBe("deferred");
        saved.Account.Portfolio.FirstImportDate.ShouldBe(new DateOnly(2024, 3, 3));
        HoldingRequests.ShouldBeEmpty();
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(0);

        await Client.StartConnectAsync(account.Id, Ct);
        using var callback = await Client.DhanCallbackAsync(TokenId, Ct);
        var run = await Client.SyncAsync(account.Id, Ct);

        (run.Trigger, run.Status, run.NewCount).ShouldBe(("import", "succeeded", 4));
        (await Factory.HoldingAsync("BETA", Ct)).Lots.Single().ShouldSatisfyAllConditions(
            l => l.AcquiredOn.ShouldBe(new DateOnly(2024, 3, 3)),
            l => l.UsesDefaultDate.ShouldBeTrue(),
            l => l.Source.ShouldBe(LotSource.Import));
    }

    [Fact]
    public async Task The_first_sync_without_a_stored_date_dates_the_holdings_2025_01_01()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();

        var run = await Client.SyncAsync(account.Id, Ct);

        run.Trigger.ShouldBe("import");
        (await Factory.HoldingAsync("ALPHA", Ct)).Lots.Single().AcquiredOn.ShouldBe(DefaultDate);
        (await Client.SyncStatusAsync(account.Id, Ct)).ShouldSatisfyAllConditions(
            s => s.HasHoldings.ShouldBeTrue(),
            s => s.FirstImportDate.ShouldBe(DefaultDate));
    }

    [Fact]
    public async Task The_standalone_import_endpoint_does_not_exist()
    {
        var account = await ConnectedAccountAsync();

        using var response = await Client.PostJsonAsync($"/api/accounts/{account.Id}/holdings/import", new { rows = Array.Empty<object>() }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_expired_token_counts_as_not_connected()
    {
        var account = await ConnectedAccountAsync();
        Factory.Clock.Advance(TimeSpan.FromHours(21)); // Dhan stated 20 hours; the owner's session has ended meanwhile
        var later = await Factory.SignInAsync(Ct);

        using var response = await later.Client.PostAsync($"/api/accounts/{account.Id}/holdings/preview", Ct);

        (response.StatusCode, (await response.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "portfolio.not_connected"));
        HoldingRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_dhan_refuses_the_token_the_settings_are_saved_and_the_import_is_reported_failed_and_recorded()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();
        Dhan.Override("/v2/holdings", _ => FakeBrokers.Json(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"expired"}"""));

        var saved = await Client.SavePortfolioAsync(account.Id, new { refreshMode = "daily", firstImportDate = "2024-01-01", firstImport = new { rows = new[] { new { key = "INE000A01011" } } } }, Ct);

        var import = saved.Import!;
        (import.Status, import.ErrorCode, import.Cause).ShouldBe(("failed", "portfolio.import_failed", "portfolio.not_connected"));
        import.Message.ShouldNotBeNullOrEmpty();
        (saved.Account.Portfolio.RefreshMode, saved.Account.Portfolio.FirstImportDate, saved.Account.Portfolio.HasHoldings).ShouldBe(("daily", new DateOnly(2024, 1, 1), false));
        var run = (await Factory.SyncsAsync(account.Id, Ct)).Single();
        (run.Trigger, run.Status).ShouldBe((SyncTrigger.Import, SyncStatus.Failed));
        (await Factory.CountAsync("portfolio.holdings", Ct)).ShouldBe(0);
        AssertNoSecret(import.Message!);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    public async Task A_dhan_outage_is_a_502_broker_error_and_a_failed_run(int status)
    {
        var account = await ConnectedAccountAsync();
        Dhan.Override("/v2/holdings", _ => FakeBrokers.Json((HttpStatusCode)status, """{"errorCode":"DH-908"}"""));

        using var response = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/preview", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.broker_error");
        AssertNoSecret(await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task No_secret_and_no_raw_dhan_data_is_in_responses_audit_or_logs()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();
        var bodies = new List<string>();
        using (var preview = await Client.PostAsync($"/api/accounts/{account.Id}/holdings/preview", Ct))
        {
            bodies.Add(await preview.Content.ReadAsStringAsync(Ct));
        }

        using (var import = await Client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { firstImport = new { rows = new[] { new { key = "INE000A01011" } } } }, Ct))
        {
            bodies.Add(await import.Content.ReadAsStringAsync(Ct));
        }

        using (var list = await Client.GetAsync("/api/holdings", Ct))
        {
            bodies.Add(await list.Content.ReadAsStringAsync(Ct));
        }

        using (var activity = await Client.GetAsync($"/api/accounts/{account.Id}/activity", Ct))
        {
            bodies.Add(await activity.Content.ReadAsStringAsync(Ct));
        }

        foreach (var body in bodies)
        {
            AssertNoSecret(body);
            body.ShouldNotContain("collateralQty"); // Dhan's raw payload fields are not passed on
        }

        Factory.Logs.All.ShouldAllBe(line => !line.Contains(AccountsApi.Marker, StringComparison.Ordinal));
        Factory.Logs.All.ShouldAllBe(line => !line.Contains("/v2/holdings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task While_a_sync_runs_the_import_of_a_save_fails_but_the_settings_are_kept()
    {
        var account = await ConnectedAccountAsync();
        DhanHolds();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dhan.BeforeHoldingsAnswer = async () =>
        {
            arrived.TrySetResult();
            await release.Task;
        };
        var slow = Client.PostAsync($"/api/accounts/{account.Id}/holdings/sync", Ct);
        await arrived.Task.WaitAsync(Ct);

        var saved = await Client.SavePortfolioAsync(account.Id, new { refreshMode = "daily", firstImport = new { rows = new[] { new { key = "INE000A01011" } } } }, Ct);
        release.SetResult();
        using var first = await slow;

        (saved.Import!.Status, saved.Import.Cause).ShouldBe(("failed", "portfolio.sync_running"));
        saved.Account.Portfolio.RefreshMode.ShouldBe("daily");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
