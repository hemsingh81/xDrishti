using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The holdings quote poll end to end (ADR 0019) on the real database with the API's real quote client over the fake Dhan server: Run now, the one-statement update across accounts, closed and unmapped holdings,
/// the sync-after-quote rule, failures, the new fields and sort of the holdings API, the summary equal to the rows, the migration's columns and the speed of the reads. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingQuotesTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private const string QuotePath = "/v2/marketfeed/quote";

    private const string Service = "holdings-quotes";

    private static readonly string[] OneDay = ["mon"];

    private static readonly string[] WeekDays = ["mon", "tue", "wed", "thu", "fri"];

    private static readonly object BothRoles = new { data = new { enabled = true, kind = "primary" }, portfolio = new { enabled = true } };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
    }

    private async Task<AccountDetail> AccountAsync(string name, string clientId, object? roles = null)
    {
        Factory.Brokers.DhanClientId = clientId;
        var account = await Client.CreateAccountAsync(AccountsApi.NewAccount(name, clientId, roles: roles ?? new { portfolio = new { enabled = true } }), Ct);
        await Client.StartConnectAsync(account.Id, Ct);
        using var callback = await Client.DhanCallbackAsync(TokenId, Ct);
        callback.Location().ShouldBe(ConnectApi.Connected(account.Id));
        return account;
    }

    /// <summary>Two connected accounts (the first is the primary Data account too) that both hold ALPHA (100) and BETA (10), mapped to the master, and BSEONLY (not on the NSE).</summary>
    private async Task<(AccountDetail One, AccountDetail Two)> TwoAccountsAsync()
    {
        await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        await Factory.SeedInstrumentAsync("BETA", "INE000B01012", "1002", "Beta Limited", Ct);
        var one = await AccountAsync("Example One", "1000000001", BothRoles);
        var two = await AccountAsync("Example Two", "1000000002");
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("BSEONLY", 7, 10m, 11m, "5555", null, "BSE"),
        ]);
        await ImportAllAsync(one.Id, "2024-10-12");
        await ImportAllAsync(two.Id, "2024-10-12");
        return (one, two);
    }

    private async Task<ServiceRunView> RunNowAsync() => await Factory.RunNowAsync(Client, Service, Ct);

    private List<FakeBrokers.RecordedRequest> QuoteRequests => [.. Dhan.Requests.Where(r => r.Uri.AbsolutePath == QuotePath)];

    private async Task<T> ScalarAsync<T>(string sql) =>
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync(Ct))!;
        });

    [Fact]
    public async Task Run_now_quotes_the_open_nse_holdings_of_every_account_in_one_statement_and_leaves_the_rest_alone()
    {
        var (one, two) = await TwoAccountsAsync();
        Dhan.Quotes["1001"] = (63m, 60m);
        Dhan.Quotes["1002"] = (171m, 180m);
        Dhan.Holdings.RemoveAll(h => h.Symbol == "BETA");
        Ok(await Client.SyncAsync(two.Id, Ct)); // BETA is closed in the second account (one of three: not "most")
        Dhan.Holdings.Add(Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"));

        var run = await RunNowAsync();

        (run.Status, run.Message, run.Trigger).ShouldBe(("succeeded", "Quoted 2 of 3 holdings; 1 not on NSE", "manual"));
        var request = QuoteRequests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Header("access-token").ShouldBe(FakeBrokers.DhanAccessToken);
        request.Header("client-id").ShouldBe("1000000001"); // the primary Data account's client id
        using (var body = JsonDocument.Parse(request.Body!))
        {
            body.RootElement.GetProperty("NSE_EQ").EnumerateArray().Select(e => e.GetInt64()).Order().ShouldBe([1001L, 1002L]);
        }

        var rows = (await Client.ListHoldingsAsync("?status=all&pageSize=100", Ct)).Items;
        var alpha = rows.Where(r => r.Symbol == "ALPHA").ToList();
        alpha.Count.ShouldBe(2);
        alpha.ShouldAllBe(r => r.LastPrice == 63m && r.PreviousClose == 60m && r.PriceSource == "quote" && IsAround(r.PriceAt, MondayMorning) && r.PriceAt == r.LastPriceAt);
        alpha.ShouldAllBe(r => r.DayChange == 300m && r.DayChangePct == 5m);
        var betaOpen = rows.Single(r => r is { Symbol: "BETA", Status: "open" });
        (betaOpen.AccountId, betaOpen.LastPrice, betaOpen.PreviousClose, betaOpen.DayChange).ShouldBe((one.Id, 171m, 180m, -90m));
        var betaClosed = rows.Single(r => r is { Symbol: "BETA", Status: "closed" });
        (betaClosed.AccountId, betaClosed.LastPrice, betaClosed.PreviousClose, betaClosed.PriceSource).ShouldBe((two.Id, 180m, null, "sync")); // a closed holding is not quoted
        var bse = rows.Where(r => r.Symbol == "BSEONLY").ToList();
        bse.ShouldAllBe(r => r.LastPrice == 11m && r.PriceSource == "sync" && r.PreviousClose == null && r.DayChange == null && r.LastPriceAt == r.SyncedAt);

        // every row of the batch was written by the same statement (one transaction id)
        (await ScalarAsync<long>("SELECT count(DISTINCT xmin::text) FROM portfolio.holdings WHERE last_price_source = 'quote'")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM portfolio.holdings WHERE last_price_source = 'quote'")).ShouldBe(3);
    }

    [Fact]
    public async Task The_summary_day_change_equals_the_sum_of_the_rows_and_value_pnl_and_xirr_follow_the_quote()
    {
        await TwoAccountsAsync();
        var before = await Client.SummaryAsync("", Ct);
        Dhan.Quotes["1001"] = (63m, 60m);
        Dhan.Quotes["1002"] = (171m, 180m);

        (await RunNowAsync()).Status.ShouldBe("succeeded");

        var summary = await Client.SummaryAsync("", Ct);
        var rows = (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Items;
        summary.DayChange.ShouldBe(rows.Sum(r => r.DayChange ?? 0m));
        (summary.DayChange, summary.DayChangePct, summary.DayChangeExcluded).ShouldBe((420m, 2.6923m, 2)); // 2 x (300 - 90) on 2 x (6,000 + 1,800) = 15,600; the two BSEONLY rows are left out
        IsAround(summary.PricesAsOf.Newest, MondayMorning).ShouldBeTrue();
        summary.PricesAsOf.Oldest.ShouldBe(before.PricesAsOf.Oldest);
        (summary.Returns.Value - before.Returns.Value, summary.Returns.Pnl - before.Returns.Pnl).ShouldBe((420m, 420m)); // 2 x (100 x +3 + 10 x -9)
        summary.Returns.Xirr.ShouldNotBe(before.Returns.Xirr);
        (await Client.SummaryAsync($"?accountId={rows[0].AccountId}", Ct)).DayChange.ShouldBe(rows.Where(r => r.AccountId == rows[0].AccountId).Sum(r => r.DayChange ?? 0m));
    }

    [Fact]
    public async Task The_day_change_sort_fields_work_over_http_and_are_documented()
    {
        await TwoAccountsAsync();
        Dhan.Quotes["1001"] = (63m, 60m);
        Dhan.Quotes["1002"] = (171m, 180m);
        await RunNowAsync();

        (await Client.ListHoldingsAsync("?sort=dayChange&pageSize=100", Ct)).Items.Select(i => i.Symbol).ShouldBe(["BETA", "BETA", "ALPHA", "ALPHA", "BSEONLY", "BSEONLY"]);
        (await Client.ListHoldingsAsync("?sort=-dayChangePct&pageSize=100", Ct)).Items.Select(i => i.Symbol).ShouldBe(["ALPHA", "ALPHA", "BETA", "BETA", "BSEONLY", "BSEONLY"]);
        using var bad = await Client.GetAsync("/api/holdings?sort=dayChanges", Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid");
    }

    [Fact]
    public async Task The_json_carries_the_new_fields_with_their_wire_names()
    {
        await TwoAccountsAsync();
        Dhan.Quotes["1001"] = (63m, 60m);
        await RunNowAsync();

        using var list = await Client.GetAsync("/api/holdings?pageSize=1&sort=symbol", Ct);
        var row = JsonDocument.Parse(await list.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("items")[0];
        foreach (var name in new[] { "previousClose", "dayChange", "dayChangePct", "priceAt", "priceSource" })
        {
            row.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        row.GetProperty("priceSource").GetString().ShouldBe("quote");
        using var summary = await Client.GetAsync("/api/holdings/summary", Ct);
        var json = JsonDocument.Parse(await summary.Content.ReadAsStringAsync(Ct)).RootElement;
        foreach (var name in new[] { "dayChange", "dayChangePct", "dayChangeExcluded", "pricesAsOf" })
        {
            json.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "oldest", "newest" })
        {
            json.GetProperty("pricesAsOf").TryGetProperty(name, out _).ShouldBeTrue(name);
        }
    }

    [Fact]
    public async Task A_sync_on_the_same_india_day_keeps_the_quote_and_a_sync_on_a_later_day_takes_the_synced_price()
    {
        var (one, _) = await TwoAccountsAsync();
        Dhan.Quotes["1001"] = (63m, 60m);
        await RunNowAsync();
        Dhan.Holdings[0] = Holding("ALPHA", 100, 50m, 61m, "1001", "INE000A01011");

        Ok(await Client.SyncAsync(one.Id, Ct));

        var same = (await Client.ListHoldingsAsync($"?accountId={one.Id}&q=ALPHA", Ct)).Items.Single();
        (same.LastPrice, same.PriceSource, same.PreviousClose).ShouldBe((63m, "quote", 60m));

        var tuesday00 = MondayMorning.AddHours(14.5); // Tuesday 00:30 India time: the next India day, the Dhan token (20 hours) still valid
        Factory.Clock.Pin(tuesday00);
        var tuesday = await Factory.SignInElevatedAsync(Ct);
        Ok(await tuesday.Client.SyncAsync(one.Id, Ct));

        var later = (await tuesday.Client.ListHoldingsAsync($"?accountId={one.Id}&q=ALPHA", Ct)).Items.Single();
        (later.LastPrice, later.PriceSource, later.PreviousClose).ShouldBe((61m, "sync", 60m));
        IsAround(later.PriceAt, tuesday00).ShouldBeTrue();
    }

    /// <summary>The pinned clock runs on from the pin, so a time read from the API is a few seconds after it.</summary>
    private static bool IsAround(DateTimeOffset? actual, DateTimeOffset pinned) => actual is { } at && at >= pinned && at < pinned.AddMinutes(2);

    private static SyncRunView Ok(SyncRunView run)
    {
        run.Status.ShouldBe("succeeded");
        return run;
    }

    [Fact]
    public async Task A_refused_token_fails_the_run_with_the_safe_message_changes_no_price_and_raises_the_notice()
    {
        await TwoAccountsAsync();
        Dhan.Override(QuotePath, _ => FakeBrokers.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"{{AccountsApi.Marker}}"}"""));

        var run = await RunNowAsync();

        (run.Status, run.Message).ShouldBe(("failed", "The Data account's token was refused by Dhan."));
        (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Items.ShouldAllBe(r => r.PriceSource == "sync" && r.PreviousClose == null);
        using var notices = await Client.GetAsync("/api/services/notices", Ct);
        var text = await notices.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("service:failed:holdings-quotes:2026-10-12");
        AssertNoSecret(text);
    }

    [Fact]
    public async Task Dhan_being_down_or_limiting_the_rate_fails_the_run_and_the_next_one_succeeds_and_clears_the_notice()
    {
        await TwoAccountsAsync();
        Dhan.Quotes["1001"] = (63m, 60m);
        Dhan.Override(QuotePath, _ => FakeBrokers.Json(HttpStatusCode.TooManyRequests, "{}"));
        (await RunNowAsync()).Message.ShouldBe("Dhan's rate limit was reached; the next tick tries again.");
        Dhan.Override(QuotePath, _ => FakeBrokers.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        (await RunNowAsync()).Message.ShouldBe("Dhan's quote service answered HTTP 503.");
        (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Items.ShouldAllBe(r => r.PriceSource == "sync");

        Dhan.Override(QuotePath, request => FakeBrokers.Json(HttpStatusCode.OK, """{"data":{"NSE_EQ":{"1001":{"last_price":63,"ohlc":{"close":60}}}},"status":"success"}"""));
        var ok = await RunNowAsync();

        (ok.Status, ok.Message).ShouldBe(("succeeded", "Quoted 1 of 3 holdings; 1 not on NSE; 1 without a quote"));
        using var notices = await Client.GetAsync("/api/services/notices", Ct);
        (await notices.Content.ReadAsStringAsync(Ct)).ShouldNotContain("service:failed");
    }

    [Fact]
    public async Task Without_a_connected_data_account_the_run_is_skipped_with_the_reason_and_no_request_is_made()
    {
        await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        Factory.Brokers.DhanClientId = "1000000001";
        await Client.CreateAccountAsync(AccountsApi.NewAccount("Example One", "1000000001", roles: BothRoles), Ct); // the primary Data account, never connected
        var connected = await AccountAsync("Example Two", "1000000002");
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        await ImportAllAsync(connected.Id, "2024-10-12");

        var run = await RunNowAsync();

        (run.Status, run.Message).ShouldBe(("skipped", "The Data account 'Example One' is not connected to Dhan. Connect it under Accounts."));
        QuoteRequests.ShouldBeEmpty();
        (await Client.ListHoldingsAsync("", Ct)).Items.ShouldAllBe(r => r.PriceSource == "sync");
    }

    [Fact]
    public async Task Without_open_holdings_the_run_succeeds_and_makes_no_request()
    {
        await AccountAsync("Example One", "1000000001", BothRoles);

        var run = await RunNowAsync();

        (run.Status, run.Message).ShouldBe(("succeeded", "No open holdings"));
        QuoteRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_service_is_listed_with_its_defaults_and_accepts_only_an_interval_schedule()
    {
        using var list = await Client.GetAsync("/api/services", Ct);
        var view = (await list.ReadAsync<ServiceList>(Ct)).Items.Single(s => s.Id == Service);

        (view.Name, view.RunsAs, view.Enabled, view.IsDefault, view.Status).ShouldBe(("Holdings quotes", "xd-api", true, true, "healthy"));
        view.AllowedScheduleTypes.ShouldBe(["interval"]);
        (view.Schedule.Type, view.Schedule.From, view.Schedule.To, view.Schedule.EveryMinutes, view.Schedule.TradingDaysOnly).ShouldBe(("interval", "09:15", "15:30", 5, true));
        (view.Execution.Retries, view.Execution.TimeoutMinutes, view.Execution.CatchUp, view.Execution.NotifyOnFailure).ShouldBe((0, 2, false, true));
        view.NextRun.ShouldBe(MondayMorning.AddMinutes(5)); // 10:05 India time
        view.Issues.ShouldBeEmpty();

        using var daily = await Client.PutJsonAsync($"/api/services/{Service}/schedule",
            new { enabled = true, schedule = new { type = "daily", time = "10:00", days = OneDay, tradingDaysOnly = true }, retries = 0, timeoutMinutes = 2, catchUp = false, notifyOnFailure = true, revision = 0 }, Ct);
        daily.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var narrower = await Client.PutJsonAsync($"/api/services/{Service}/schedule",
            new { enabled = true, schedule = new { type = "interval", from = "10:00", to = "15:30", everyMinutes = 5, days = WeekDays, tradingDaysOnly = true }, retries = 0, timeoutMinutes = 2, catchUp = false, notifyOnFailure = true, revision = 0 }, Ct);
        var saved = await narrower.ReadAsync<ServiceView>(Ct);
        saved.Issues.ShouldContain(i => i.Code == "window_not_covering" && i.Severity == "warning");
    }

    [Fact]
    public async Task The_migration_added_the_columns_and_the_database_refuses_another_price_source()
    {
        await TwoAccountsAsync();

        (await ScalarAsync<string>("SELECT data_type || '/' || numeric_precision || '/' || numeric_scale FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'holdings' AND column_name = 'previous_close'")).ShouldBe("numeric/18/4");
        (await ScalarAsync<string>("SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'holdings' AND column_name = 'previous_close'")).ShouldBe("YES");
        (await ScalarAsync<string>("SELECT is_nullable || '/' || column_default FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = 'holdings' AND column_name = 'last_price_source'")).ShouldBe("NO/'sync'::character varying");
        var error = await Should.ThrowAsync<PostgresException>(async () => await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("UPDATE portfolio.holdings SET last_price_source = 'other'", Ct)));
        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    // ---- the review: races, nulls, the SQL against the domain rules, the locks, the accounts ----

    private async Task ExecAsync(string sql, params (string Name, object Value)[] parameters) =>
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(sql, [.. parameters.Select(p => new NpgsqlParameter(p.Name, p.Value))], Ct);
            return 0;
        });

    private async Task<Guid> InstrumentOfAsync(string symbol) =>
        await ScalarAsync<Guid>($"SELECT instrument_id FROM portfolio.holdings WHERE trading_symbol = '{symbol}' LIMIT 1");

    private async Task<(decimal Last, decimal? Previous, string Source, DateTimeOffset? At)> RowAsync(Guid accountId, string symbol) =>
        await Factory.WithDbAsync(async db =>
        {
            var h = await db.Holdings.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.TradingSymbol == symbol, Ct);
            return (h.LastPrice, h.PreviousClose, h.LastPriceSource.ToString().ToLowerInvariant(), h.LastPriceAt);
        });

    private async Task<int> ApplyAsync(IReadOnlyList<HoldingQuote> quotes, DateTimeOffset at)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IHoldingQuotes>().ApplyAsync(quotes, at, Ct);
    }

    [Fact]
    public async Task A_quote_written_between_the_syncs_read_and_save_is_not_overwritten_and_the_sync_reads_again()
    {
        var (one, _) = await TwoAccountsAsync();
        var alpha = await InstrumentOfAsync("ALPHA");
        var quotedAt = Factory.Clock.GetUtcNow().AddSeconds(30);
        Dhan.Holdings[0] = Holding("ALPHA", 100, 50m, 61m, "1001", "INE000A01011");
        var fired = 0;
        await using var host = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            var original = services.Last(d => d.ServiceType == typeof(IHoldingStore));
            services.Remove(original);
            services.AddScoped<IHoldingStore>(sp => new AfterFirstListStore(
                (IHoldingStore)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!),
                async () =>
                {
                    fired++;
                    (await ApplyAsync([new HoldingQuote(alpha, 70m, 66m)], quotedAt)).ShouldBe(2); // the quote lands after the sync read the rows, before it saves
                }));
        }));

        using var scope = host.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<SyncHoldingsCommand, SyncRunView>>()
            .HandleAsync(new SyncHoldingsCommand(one.Id, XDrishti.Domain.Portfolio.SyncTrigger.Manual, "owner@example.invalid"), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Status.ShouldBe("succeeded");
        fired.ShouldBe(1);
        var row = await RowAsync(one.Id, "ALPHA");
        (row.Last, row.Previous, row.Source, row.At).ShouldBe((70m, 66m, "quote", quotedAt)); // the sync saw the quote of the same India day and kept it
        (await RowAsync(one.Id, "BETA")).Source.ShouldBe("sync"); // the rest of the sync was applied
    }

    private sealed class AfterFirstListStore(IHoldingStore inner, Func<Task> after) : IHoldingStore
    {
        private bool _done;

        public ValueTask<bool> AnyForAccountAsync(Guid accountId, CancellationToken cancellationToken) => inner.AnyForAccountAsync(accountId, cancellationToken);

        public ValueTask<bool> AnyAppearedSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken) => inner.AnyAppearedSinceAsync(accountId, since, cancellationToken);

        public async ValueTask<IReadOnlyList<XDrishti.Domain.Portfolio.Holding>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken)
        {
            var read = await inner.ListForAccountAsync(accountId, cancellationToken);
            if (!_done)
            {
                _done = true;
                await after();
            }

            return read;
        }

        public ValueTask<XDrishti.Domain.Portfolio.Holding?> FindAsync(Guid holdingId, CancellationToken cancellationToken) => inner.FindAsync(holdingId, cancellationToken);

        public void Add(XDrishti.Domain.Portfolio.Holding holding) => inner.Add(holding);

        public void Touch(XDrishti.Domain.Portfolio.Holding holding) => inner.Touch(holding);
    }

    [Fact]
    public async Task A_quote_without_a_previous_close_updates_the_price_and_keeps_the_previous_close_through_the_real_sql()
    {
        var (one, two) = await TwoAccountsAsync();
        var alpha = await InstrumentOfAsync("ALPHA");
        var beta = await InstrumentOfAsync("BETA");
        var t = Factory.Clock.GetUtcNow();
        await ApplyAsync([new HoldingQuote(alpha, 63m, 60m), new HoldingQuote(beta, 171m, 180m)], t);

        var updated = await ApplyAsync([new HoldingQuote(alpha, 64m, null), new HoldingQuote(beta, 172m, 181m)], t.AddMinutes(5)); // a null among the values of the arrays

        updated.ShouldBe(4);
        foreach (var account in new[] { one.Id, two.Id })
        {
            var a = await RowAsync(account, "ALPHA");
            (a.Last, a.Previous, a.Source, a.At).ShouldBe((64m, 60m, "quote", t.AddMinutes(5))); // price updated, previous close kept
            (await RowAsync(account, "BETA")).Previous.ShouldBe(181m); // a quote with a previous close replaces it
        }
    }

    // initial previous close, last, previous close of the quote, open, the initial price is a quote
    private static readonly (decimal? Initial, decimal Last, decimal? Previous, bool Open, bool QuoteSource)[] QuoteCases =
    [
        (null, 63m, 60m, true, true),
        (58m, 63m, 60m, true, true),
        (58m, 63m, null, true, true), // missing: kept
        (58m, 63m, 0m, true, true), // zero: kept
        (58m, 63m, -2m, true, true), // negative: kept
        (null, 63m, null, true, true), // nothing to keep
        (58m, 0m, 60m, true, true), // no last price: no quote at all
        (58m, -1m, 60m, true, true),
        (58m, 63m, 60m, false, true), // a closed holding is not quoted
        (58m, 63m, null, true, false), // the price was a sync price of an earlier day: the previous close is kept all the same
        (58m, 63m, 60m, true, false),
    ];

    [Fact]
    public async Task The_sql_batch_update_and_Holding_ApplyQuote_agree_on_every_case()
    {
        var (one, _) = await TwoAccountsAsync();
        var alpha = await InstrumentOfAsync("ALPHA");
        var t0 = new DateTimeOffset(2026, 10, 9, 5, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 10, 11, 5, 0, 0, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);
        var row60 = new XDrishti.Domain.Portfolio.BrokerHolding("ALL", "1001", "INE000A01011", "ALPHA", 100, 50m, 60m);

        foreach (var (initialPrevious, last, previous, open, quoteSource) in QuoteCases)
        {
            var label = $"initial {initialPrevious}, last {last}, previous {previous}, open {open}, quote source {quoteSource}";

            // the domain
            var holding = XDrishti.Domain.Portfolio.Holding.Open(Guid.NewGuid(), Guid.NewGuid(), alpha, null, row60, new DateOnly(2024, 10, 12), false, XDrishti.Domain.Portfolio.LotSource.Import, t0);
            var initialAt = t0;
            if (initialPrevious is { } p0)
            {
                holding.ApplyQuote(60m, p0, t0);
                if (!quoteSource)
                {
                    holding.Reconcile(row60, alpha, null, new DateOnly(2026, 10, 11), t1); // a later day: the sync price, the previous close stays
                    initialAt = t1;
                }
            }

            if (!open)
            {
                holding.CloseMissing(new DateOnly(2026, 10, 11), t1);
            }

            var initialSource = holding.LastPriceSource.ToString().ToLowerInvariant();
            holding.ApplyQuote(last, previous, at);

            // the database, in the same starting state
            await ExecAsync(
                "UPDATE portfolio.holdings SET last_price = 60, last_price_at = @at, previous_close = @prev, last_price_source = @src, status = @status WHERE account_id = @account AND trading_symbol = 'ALPHA'",
                ("at", initialAt), ("prev", (object?)initialPrevious ?? DBNull.Value), ("src", initialSource), ("status", open ? "open" : "closed"), ("account", one.Id));
            await ApplyAsync([new HoldingQuote(alpha, last, previous)], at);
            var row = await RowAsync(one.Id, "ALPHA");

            (row.Last, row.Previous, row.Source, row.At).ShouldBe(
                (holding.LastPrice, holding.PreviousClose, holding.LastPriceSource.ToString().ToLowerInvariant(), holding.LastPriceAt), label);
        }
    }

    [Fact]
    public async Task A_row_a_running_sync_holds_is_left_for_the_next_tick_and_the_batch_never_waits_for_it()
    {
        var (one, two) = await TwoAccountsAsync();
        var alpha = await InstrumentOfAsync("ALPHA");
        var t = Factory.Clock.GetUtcNow();
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using (var transaction = await connection.BeginTransactionAsync(Ct))
        {
            await using (var lockRow = new NpgsqlCommand("UPDATE portfolio.holdings SET synced_at = now() WHERE account_id = @a AND trading_symbol = 'ALPHA'", connection, transaction))
            {
                lockRow.Parameters.AddWithValue("a", one.Id);
                await lockRow.ExecuteNonQueryAsync(Ct);
            }

            var updated = await ApplyAsync([new HoldingQuote(alpha, 63m, 60m)], t).WaitAsync(TimeSpan.FromSeconds(20), Ct); // would hang if it waited for the lock

            updated.ShouldBe(1);
            (await RowAsync(two.Id, "ALPHA")).Source.ShouldBe("quote");
            (await RowAsync(one.Id, "ALPHA")).Source.ShouldBe("sync");
            await transaction.RollbackAsync(Ct);
        }

        (await ApplyAsync([new HoldingQuote(alpha, 63m, 60m)], t)).ShouldBe(2);
    }

    [Fact]
    public async Task The_update_reads_the_same_accounts_as_the_targets_active_ones_with_the_portfolio_role()
    {
        var (one, two) = await TwoAccountsAsync();
        var alpha = await InstrumentOfAsync("ALPHA");
        var t = Factory.Clock.GetUtcNow();
        using var scope = Factory.Services.CreateScope();
        var quotes = scope.ServiceProvider.GetRequiredService<IHoldingQuotes>();
        (await quotes.ListTargetsAsync(Ct)).OpenSecurities.ShouldBe(3);

        await ExecAsync("UPDATE accounts.portfolio_settings SET enabled = false WHERE account_id = @a", ("a", two.Id));
        (await quotes.ApplyAsync([new HoldingQuote(alpha, 63m, 60m)], t, Ct)).ShouldBe(1); // only the account that is read
        (await RowAsync(two.Id, "ALPHA")).Source.ShouldBe("sync");
        await ExecAsync("UPDATE accounts.portfolio_settings SET enabled = true WHERE account_id = @a", ("a", two.Id));

        await ExecAsync("UPDATE accounts.broker_accounts SET archived_at = now() WHERE id = @a", ("a", two.Id));
        (await quotes.ApplyAsync([new HoldingQuote(alpha, 64m, 60m)], t, Ct)).ShouldBe(1);
        (await RowAsync(two.Id, "ALPHA")).Last.ShouldBe(60m);

        await ExecAsync("UPDATE accounts.portfolio_settings SET enabled = false WHERE account_id = @a", ("a", one.Id));
        var targets = await quotes.ListTargetsAsync(Ct);
        (targets.OpenSecurities, targets.Nse.Count).ShouldBe((0, 0));
        (await quotes.ApplyAsync([new HoldingQuote(alpha, 65m, 60m)], t, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Five_hundred_holdings_are_listed_and_summed_without_a_query_per_holding()
    {
        var account = await AccountAsync("Example One", "1000000001");
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, last_price_at, previous_close, last_price_source, status, first_seen_at, synced_at)
                SELECT gen_random_uuid(), {0}, (SELECT id FROM accounts.portfolio_buckets LIMIT 1), 'NSE', 'S' || n, 'SYM' || n, 'Name ' || n, 10 + n, 50, 60 + (n % 7), now(), 59, 'quote', 'open', now(), now()
                FROM generate_series(1, 500) AS n;
                INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated)
                SELECT gen_random_uuid(), h.id, date '2025-01-01', h.qty, 50, 'import', false, now(), false FROM portfolio.holdings h;
                """,
                [account.Id],
                Ct);
            return 0;
        });
        (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Total.ShouldBe(500); // warm up

        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var page = await Client.ListHoldingsAsync("?sort=-dayChange&pageSize=100", Ct);
            var summary = await Client.SummaryAsync("", Ct);
            watch.Stop();
            best = watch.Elapsed < best ? watch.Elapsed : best;
            page.Items.Count.ShouldBe(100);
            summary.DayChangeExcluded.ShouldBe(0);
        }

        // Not a benchmark (the 200 ms of the spec is the owner's measure): a generous bound that only a query per holding would break.
        best.TotalMilliseconds.ShouldBeLessThan(3000, $"list + summary took {best.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms");
    }
}
