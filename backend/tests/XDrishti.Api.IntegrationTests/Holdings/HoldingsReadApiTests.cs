using System.Diagnostics;
using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>The list, summary, lots and date edit over HTTP (clock: Monday 2026-10-12 10:00 India time).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsReadApiTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private const string TwoYearsAgo = "2024-10-12";

    private async Task<AccountDetail> AccountWithHoldingsAsync(string name = "Example One", string clientId = AccountsApi.ClientId, string date = TwoYearsAgo)
    {
        var account = await ConnectedAccountAsync(name, clientId);
        Dhan.Holdings.Clear();
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("GAMMA", 5, 1000m, 1500m, "1003", "INE000G01013"),
        ]);
        await ImportAllAsync(account.Id, date);
        return account;
    }

    [Fact]
    public async Task The_list_returns_rows_with_lots_summary_and_every_returns_field()
    {
        await AccountWithHoldingsAsync();

        var page = await Client.ListHoldingsAsync("?sort=symbol", Ct);

        page.Items.Select(i => i.Symbol).ShouldBe(["ALPHA", "BETA", "GAMMA"]);
        var alpha = page.Items[0];
        (alpha.LotCount, alpha.EarliestAcquiredOn, alpha.UsesDefaultDate, alpha.Status, alpha.AccountName, alpha.BucketName)
            .ShouldBe((1, new DateOnly(2024, 10, 12), false, "open", "Example One", "Example One · Portfolio"));
        (alpha.Returns.Invested, alpha.Returns.Value, alpha.Returns.Pnl, alpha.Returns.PnlPct, alpha.Returns.XirrState).ShouldBe((5000m, 6000m, 1000m, 20m, "ok"));
        alpha.Returns.Xirr.ShouldNotBeNull().ShouldBe(9.5445m, 0.001m); // 20 % over two years: 9.54 % a year
        (alpha.Returns.RealisedPnl, alpha.Returns.RealisedEstimated).ShouldBe((0m, false));
        (page.Total, page.Page, page.PageSize).ShouldBe((3, 1, 50));
    }

    [Fact]
    public async Task The_json_uses_the_documented_names_and_wire_values()
    {
        await AccountWithHoldingsAsync();

        using var response = await Client.GetAsync("/api/holdings?pageSize=1", Ct);
        var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        var row = json.GetProperty("items")[0];
        foreach (var name in new[] { "id", "accountId", "accountName", "bucketId", "bucketName", "instrumentId", "canonicalSymbol", "mapped", "exchange", "securityId", "isin", "symbol", "name", "qty", "avgCost", "lastPrice", "lastPriceAt", "status", "firstSeenAt", "closedAt", "syncedAt", "lotCount", "earliestAcquiredOn", "usesDefaultDate", "priced", "returns" })
        {
            row.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        var returns = row.GetProperty("returns");
        foreach (var name in new[] { "invested", "value", "pnl", "pnlPct", "xirr", "xirrState", "realisedPnl", "realisedEstimated" })
        {
            returns.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        row.GetProperty("earliestAcquiredOn").GetString().ShouldBe(TwoYearsAgo);
        returns.GetProperty("xirrState").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task Paging_sorting_and_filters_work_and_bad_values_are_400()
    {
        await AccountWithHoldingsAsync();

        (await Client.ListHoldingsAsync("?sort=-pnl", Ct)).Items.Select(i => i.Symbol).ShouldBe(["GAMMA", "ALPHA", "BETA"]);
        (await Client.ListHoldingsAsync("?sort=symbol&pageSize=2&page=2", Ct)).Items.Select(i => i.Symbol).ShouldBe(["GAMMA"]);
        (await Client.ListHoldingsAsync("?q=ine000b", Ct)).Items.Select(i => i.Symbol).ShouldBe(["BETA"]);
        (await Client.ListHoldingsAsync("?q=alp", Ct)).Items.Select(i => i.Symbol).ShouldBe(["ALPHA"]);
        (await Client.ListHoldingsAsync("?status=closed", Ct)).Total.ShouldBe(0);
        foreach (var query in new[] { "?pageSize=101", "?pageSize=0", "?page=0", "?sort=bogus", "?status=weird" })
        {
            using var response = await Client.GetAsync("/api/holdings" + query, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid", query);
        }
    }

    [Fact]
    public async Task Filtering_by_account_and_bucket_isolates_them_and_the_summary_is_the_sum_of_the_rows()
    {
        var one = await AccountWithHoldingsAsync();
        var two = await ConnectedAccountAsync("Example Two", "1000000002");
        Dhan.Holdings.Clear();
        Dhan.Holdings.Add(Holding("DELTA", 8, 25m, 26m, "1004", "INE000D01014"));
        await ImportAllAsync(two.Id, TwoYearsAgo);
        var bucketTwo = (await (await Client.GetAsync("/api/portfolio-buckets", Ct)).ReadAsync<List<XDrishti.Application.Accounts.Buckets.BucketView>>(Ct)).Single(b => b.Name == "Example Two · Portfolio");

        var all = await Client.ListHoldingsAsync("", Ct);
        var oneRows = await Client.ListHoldingsAsync($"?accountId={one.Id}", Ct);
        var byBucket = await Client.ListHoldingsAsync($"?bucketId={bucketTwo.Id}", Ct);
        var total = await Client.SummaryAsync("", Ct);
        var summaryOne = await Client.SummaryAsync($"?accountId={one.Id}", Ct);
        var summaryBucket = await Client.SummaryAsync($"?bucketId={bucketTwo.Id}", Ct);

        all.Total.ShouldBe(4);
        oneRows.Items.Select(i => i.Symbol).ShouldBe(["ALPHA", "BETA", "GAMMA"], ignoreOrder: true);
        byBucket.Items.Select(i => i.Symbol).ShouldBe(["DELTA"]);
        total.Returns.Invested.ShouldBe(all.Items.Sum(i => i.Returns.Invested));
        total.Returns.Value.ShouldBe(all.Items.Sum(i => i.Returns.Value));
        total.Returns.Pnl.ShouldBe(all.Items.Sum(i => i.Returns.Pnl));
        (summaryOne.Returns.Invested, summaryOne.OpenCount).ShouldBe((12000m, 3));
        (summaryBucket.Returns.Invested, summaryBucket.OpenCount).ShouldBe((200m, 1));
        total.Returns.Invested.ShouldBe(summaryOne.Returns.Invested + summaryBucket.Returns.Invested);
    }

    [Fact]
    public async Task The_summary_has_totals_a_group_xirr_the_default_date_count_and_the_last_sync()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange([Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"), Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012")]);
        await Client.ImportAsync(account.Id, TwoYearsAgo, new object[] { new { key = "INE000A01011" }, new { key = "INE000B01012", acquiredOn = "2026-04-12" } }, Ct);

        var summary = await Client.SummaryAsync("", Ct);

        (summary.Returns.Invested, summary.Returns.Value, summary.Returns.Pnl, summary.Returns.XirrState).ShouldBe((7000m, 7800m, 800m, "ok"));
        summary.Returns.Xirr.ShouldNotBeNull().ShouldBe(7.0556m, 0.001m); // reference: Python bisection, flows -5,000 / -2,000 / +7,800
        (summary.OpenCount, summary.ClosedCount, summary.UnmappedCount, summary.DefaultDateLots, summary.ValuationDate).ShouldBe((2, 0, 2, 1, new DateOnly(2026, 10, 12)));
        (summary.LastSync!.Trigger, summary.LastSync.Status).ShouldBe(("import", "succeeded"));
        using var unknown = await Client.GetAsync($"/api/holdings/summary?accountId={Guid.NewGuid()}", Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lots_show_figures_per_lot_and_the_date_edit_recalculates_and_clears_the_default_flag()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        await Client.ImportAsync(account.Id, "2025-01-01", new[] { new { key = "INE000A01011" } }, Ct);
        var holding = (await Client.ListHoldingsAsync("", Ct)).Items.Single();
        holding.UsesDefaultDate.ShouldBeTrue();
        var lots = await Client.LotsAsync(holding.Id, Ct);
        var lot = lots.Lots.Single();
        (lot.AcquiredOn, lot.UsesDefaultDate, lot.Status, lot.Source).ShouldBe((new DateOnly(2025, 1, 1), true, "open", "import"));

        using var response = await Client.PutJsonAsync($"/api/holdings/{holding.Id}/lots/{lot.Id}", new { acquiredOn = TwoYearsAgo }, Ct);
        var changed = await response.ReadAsync<XDrishti.Application.Portfolio.LotRow>(Ct);

        (changed.AcquiredOn, changed.UsesDefaultDate).ShouldBe((new DateOnly(2024, 10, 12), false));
        changed.Returns.Xirr.ShouldNotBeNull().ShouldBe(9.5445m, 0.001m);
        var after = (await Client.ListHoldingsAsync("", Ct)).Items.Single();
        (after.EarliestAcquiredOn, after.UsesDefaultDate).ShouldBe((new DateOnly(2024, 10, 12), false));
        (await Client.SummaryAsync("", Ct)).DefaultDateLots.ShouldBe(0);
        var activity = await (await Client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity[0].Event.ShouldBe("LotDateChanged");
        activity[0].Detail.ShouldBe("Lot date of ALPHA changed from 2025-01-01 to 2024-10-12");
    }

    [Fact]
    public async Task Bad_lot_dates_and_unknown_ids_are_rejected()
    {
        await AccountWithHoldingsAsync();
        var holding = (await Client.ListHoldingsAsync("?sort=symbol", Ct)).Items[0];
        var lot = (await Client.LotsAsync(holding.Id, Ct)).Lots.Single();

        using var future = await Client.PutJsonAsync($"/api/holdings/{holding.Id}/lots/{lot.Id}", new { acquiredOn = "2026-10-13" }, Ct);
        using var ancient = await Client.PutJsonAsync($"/api/holdings/{holding.Id}/lots/{lot.Id}", new { acquiredOn = "1989-12-31" }, Ct);
        using var missing = await Client.PutJsonAsync($"/api/holdings/{holding.Id}/lots/{lot.Id}", new { }, Ct);
        using var noLot = await Client.PutJsonAsync($"/api/holdings/{holding.Id}/lots/{Guid.NewGuid()}", new { acquiredOn = TwoYearsAgo }, Ct);
        using var noHolding = await Client.PutJsonAsync($"/api/holdings/{Guid.NewGuid()}/lots/{lot.Id}", new { acquiredOn = TwoYearsAgo }, Ct);
        using var lotsOfNone = await Client.GetAsync($"/api/holdings/{Guid.NewGuid()}/lots", Ct);

        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await future.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["acquiredOn"]);
        ancient.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (noLot.StatusCode, (await noLot.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.NotFound, "portfolio.not_found"));
        noHolding.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        lotsOfNone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.LotsAsync(holding.Id, Ct)).Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2024, 10, 12));
    }

    [Fact]
    public async Task Closed_lots_after_a_sale_show_the_estimated_realised_result()
    {
        var account = await AccountWithHoldingsAsync();
        Dhan.Holdings[0] = Holding("ALPHA", 60, 50m, 75m, "1001", "INE000A01011"); // 40 sold
        await Client.SyncAsync(account.Id, Ct);
        var alpha = (await Client.ListHoldingsAsync("?q=ALPHA", Ct)).Items.Single();

        var lots = await Client.LotsAsync(alpha.Id, Ct);

        lots.Lots.Select(l => (l.Status, l.Qty)).ShouldBe([("open", 60L), ("closed", 40L)]);
        var closed = lots.Lots[1];
        (closed.ClosePrice, closed.Estimated, closed.ClosedOn, closed.Returns.RealisedPnl).ShouldBe((75m, true, new DateOnly(2026, 10, 12), 1000m));
        (alpha.Returns.RealisedPnl, alpha.Returns.RealisedEstimated).ShouldBe((1000m, true));
        (alpha.Returns.Invested, alpha.Returns.Value).ShouldBe((3000m, 4500m));
    }

    [Fact]
    public async Task Five_hundred_holdings_are_listed_and_summarised_quickly()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange(Enumerable.Range(1, 500).Select(i => Holding($"SYM{i:000}", 10 + i, 100m + i, 110m + i, $"{2000 + i}", $"INE{i:000000}A01{i % 10}{(i % 7)}")));
        await ImportAllAsync(account.Id, TwoYearsAgo);
        await Client.ListHoldingsAsync("?pageSize=100", Ct); // warm up
        await Client.SummaryAsync("", Ct);

        var watch = Stopwatch.StartNew();
        var page = await Client.ListHoldingsAsync("?pageSize=100&sort=-xirr", Ct);
        var listed = watch.ElapsedMilliseconds;
        watch.Restart();
        var summary = await Client.SummaryAsync("", Ct);
        var summarised = watch.ElapsedMilliseconds;

        (page.Total, page.Items.Count, summary.OpenCount).ShouldBe((500, 100, 500));
        // The target is 200 ms; the bound is generous so that a busy build machine does not make the test flaky.
        listed.ShouldBeLessThan(2000);
        summarised.ShouldBeLessThan(2000);
    }

    [Fact]
    public async Task A_holding_without_a_price_is_not_a_loss_it_has_no_value_and_stays_out_of_the_summary()
    {
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.AddRange([Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"), Holding("NEWCO", 10, 300m, 0m, "1002", "INE000N01012")]);
        await ImportAllAsync(account.Id, TwoYearsAgo);

        var rows = (await Client.ListHoldingsAsync("?sort=symbol", Ct)).Items;
        var summary = await Client.SummaryAsync("", Ct);
        using var raw = await Client.GetAsync("/api/holdings?q=NEWCO", Ct);
        var json = System.Text.Json.JsonDocument.Parse(await raw.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("items")[0];

        var unpriced = rows.Single(r => r.Symbol == "NEWCO");
        (unpriced.Priced, unpriced.Returns.Invested, unpriced.Returns.Value, unpriced.Returns.Pnl, unpriced.Returns.PnlPct, unpriced.Returns.Xirr, unpriced.Returns.XirrState)
            .ShouldBe((false, 3000m, (decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null, "no_price"));
        json.GetProperty("returns").GetProperty("value").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (summary.UnpricedCount, summary.OpenCount, summary.Returns.Invested, summary.Returns.Value, summary.Returns.Pnl).ShouldBe((1, 2, 5000m, (decimal?)6000m, (decimal?)1000m));
    }

    [Fact]
    public async Task An_omitted_bucket_or_totals_keep_their_values_and_an_empty_bucket_name_selects_the_accounts_own()
    {
        var account = await ConnectedAccountAsync();
        using var added = await Client.PostJsonAsync("/api/portfolio-buckets", new { name = "Growth" }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        async Task<AccountDetail> PutAsync(object body) => (await (await Client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", body, Ct)).ReadAsync<PortfolioSaved>(Ct)).Account;

        var custom = await PutAsync(new { defaultBucket = "Growth", includeInTotals = false });
        var kept = await PutAsync(new { refreshMode = "daily" });
        var nullKept = await PutAsync(new { defaultBucket = (string?)null, includeInTotals = (bool?)null });
        var own = await PutAsync(new { defaultBucket = "" });
        var customAgain = await PutAsync(new { defaultBucket = "Growth" });
        var ownByName = await PutAsync(new { defaultBucket = "Example One · Portfolio", includeInTotals = true });

        custom.Portfolio.DefaultBucket.ShouldBe("Growth");
        (kept.Portfolio.DefaultBucket, kept.Portfolio.IncludeInTotals).ShouldBe(("Growth", false));
        (nullKept.Portfolio.DefaultBucket, nullKept.Portfolio.IncludeInTotals).ShouldBe(("Growth", false));
        (own.Portfolio.DefaultBucket, own.Portfolio.IncludeInTotals).ShouldBe(("Example One · Portfolio", false));
        customAgain.Portfolio.DefaultBucket.ShouldBe("Growth");
        (ownByName.Portfolio.DefaultBucket, ownByName.Portfolio.IncludeInTotals).ShouldBe(("Example One · Portfolio", true));
    }

    [Fact]
    public async Task A_daily_time_of_midnight_survives_a_round_trip()
    {
        Factory.Brokers.DhanClientId = AccountsApi.ClientId;
        var created = await Client.CreateAccountAsync(AccountsApi.NewAccount(roles: new { portfolio = new { enabled = true } }, portfolio: new { refreshMode = "daily", refreshDailyTimeIst = "00:00" }), Ct);

        var read = await Client.GetAccountAsync(created.Id, Ct);
        using var changed = await Client.PutJsonAsync($"/api/accounts/{created.Id}/portfolio", new { refreshDailyTimeIst = "00:00", refreshMode = "daily" }, Ct);

        (created.Portfolio.RefreshDailyTimeIst, read.Portfolio.RefreshDailyTimeIst).ShouldBe(("00:00", "00:00"));
        (await changed.ReadAsync<PortfolioSaved>(Ct)).Account.Portfolio.RefreshDailyTimeIst.ShouldBe("00:00");
    }
}
