using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Alerts;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The database and price-writing side of the live feed (ADR 0024, P2-10 criteria 9 to 14) on the real TimescaleDB: one statement across accounts, closed holdings, the precedence of sync, quote and live, the pre-open rule in SQL,
/// a row a sync holds, <c>market.live_quotes</c>, the heartbeat details, the flush with alerts and the notification, and what the runtime role may do. Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveFeedTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private static readonly object BothRoles = new { data = new { enabled = true, kind = "primary" }, portfolio = new { enabled = true } };

    private Guid _alpha;
    private Guid _beta;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
        Factory.Services.GetRequiredService<ILiveFlush>().Reset();
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

    /// <summary>Two connected accounts that both hold ALPHA (100 at 50, price 60), BETA (10 at 200, price 180) and BSEONLY (not mapped); the second account's BETA is closed.</summary>
    private async Task<(AccountDetail One, AccountDetail Two)> TwoAccountsAsync()
    {
        _alpha = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        _beta = await Factory.SeedInstrumentAsync("BETA", "INE000B01012", "1002", "Beta Limited", Ct);
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
        Dhan.Holdings.RemoveAll(h => h.Symbol == "BETA");
        (await Client.SyncAsync(two.Id, Ct)).Status.ShouldBe("succeeded"); // BETA is closed in the second account
        Dhan.Holdings.Insert(1, Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"));
        return (one, two);
    }

    private async Task<int> LiveAsync(params LivePrice[] prices)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILivePrices>().ApplyAsync(prices, Ct);
    }

    private async Task<int> QuoteAsync(DateTimeOffset at, params HoldingQuote[] quotes)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IHoldingQuotes>().ApplyAsync(quotes, at, Ct);
    }

    private static LivePrice Tick(Guid instrument, decimal price, DateTimeOffset at) => new(instrument, price, at);

    private async Task<HoldingRow> RowAsync(Guid accountId, string symbol) =>
        (await Client.ListHoldingsAsync($"?accountId={accountId}&q={symbol}&status=all", Ct)).Items.Single(r => r.Symbol == symbol);

    private async Task<T> ScalarAsync<T>(string sql) =>
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync(Ct))!;
        });

    private static DateTimeOffset India(int hour, int minute, int second = 0, int day = 12) => new DateTimeOffset(2026, 10, day, hour, minute, second, TimeSpan.Zero).AddMinutes(-330);

    // ---- criterion 9: one statement, every account, open holdings only ----

    [Fact]
    public async Task Two_accounts_holding_one_security_get_one_live_price_in_one_statement_and_the_closed_holding_is_untouched()
    {
        var (one, two) = await TwoAccountsAsync();
        var at = MondayMorning.AddMinutes(5);

        var updated = await LiveAsync(Tick(_alpha, 63m, at), Tick(_beta, 171m, at));

        updated.ShouldBe(3); // ALPHA in both accounts, BETA only where it is open
        var alphaOne = await RowAsync(one.Id, "ALPHA");
        var alphaTwo = await RowAsync(two.Id, "ALPHA");
        new[] { alphaOne, alphaTwo }.ShouldAllBe(r => r.LastPrice == 63m && r.PriceSource == "live" && r.PriceAt == at && r.PreviousClose == null);
        (await RowAsync(one.Id, "BETA")).LastPrice.ShouldBe(171m);
        var closed = await RowAsync(two.Id, "BETA");
        (closed.Status, closed.LastPrice, closed.PriceSource).ShouldBe(("closed", 180m, "sync")); // a closed holding is not priced
        var bse = await RowAsync(one.Id, "BSEONLY");
        (bse.LastPrice, bse.PriceSource).ShouldBe((11m, "sync")); // not mapped: no instrument, no tick
        (await ScalarAsync<long>("SELECT count(DISTINCT xmin::text) FROM portfolio.holdings WHERE last_price_source = 'live'")).ShouldBe(1); // one statement, one transaction id
        (await ScalarAsync<long>("SELECT count(*) FROM portfolio.holdings WHERE last_price_source = 'live'")).ShouldBe(3);
    }

    [Fact]
    public async Task A_tick_that_is_the_same_tick_again_or_older_changes_nothing_and_a_newer_one_replaces_it()
    {
        var (one, _) = await TwoAccountsAsync();
        var at = MondayMorning.AddMinutes(5);
        await LiveAsync(Tick(_alpha, 63m, at));

        (await LiveAsync(Tick(_alpha, 63m, at))).ShouldBe(0); // the same second and the same price
        (await LiveAsync(Tick(_alpha, 70m, at.AddSeconds(-1)))).ShouldBe(0);
        (await RowAsync(one.Id, "ALPHA")).LastPrice.ShouldBe(63m);

        (await LiveAsync(Tick(_alpha, 64.12345m, at.AddSeconds(2)))).ShouldBe(2);
        (await RowAsync(one.Id, "ALPHA")).LastPrice.ShouldBe(64.1235m);
    }

    [Fact]
    public async Task A_tick_of_the_same_second_with_another_price_is_a_new_tick_because_trade_times_are_whole_seconds()
    {
        var (one, _) = await TwoAccountsAsync();
        var at = MondayMorning.AddMinutes(5);
        await LiveAsync(Tick(_alpha, 63m, at));

        (await LiveAsync(Tick(_alpha, 63.05m, at))).ShouldBe(2);

        var row = await RowAsync(one.Id, "ALPHA");
        (row.LastPrice, row.PriceAt, row.PriceSource).ShouldBe((63.05m, at, "live"));
    }

    [Fact]
    public async Task A_tick_of_the_same_second_does_not_replace_a_quote_fetched_in_that_second_with_the_same_price()
    {
        var (one, _) = await TwoAccountsAsync();
        var at = MondayMorning.AddMinutes(5);
        await QuoteAsync(at, new HoldingQuote(_alpha, 63m, 60m));

        (await LiveAsync(Tick(_alpha, 63m, at))).ShouldBe(0);

        (await RowAsync(one.Id, "ALPHA")).PriceSource.ShouldBe("quote");
    }

    [Fact]
    public async Task A_live_tick_before_the_first_quote_of_the_day_is_left_out_of_the_day_change_and_counted_as_excluded_until_the_quote_gives_todays_close()
    {
        await TwoAccountsAsync();
        await QuoteAsync(MondayMorning.AddDays(-3), new HoldingQuote(_alpha, 59m, 58m)); // Friday's quote: its previous close is Thursday's

        await LiveAsync(Tick(_alpha, 66m, MondayMorning.AddMinutes(5)));

        var rows = (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Items;
        var alpha = rows.Where(r => r.Symbol == "ALPHA").ToList();
        alpha.ShouldAllBe(r => r.PriceSource == "live" && r.PreviousClose == 58m && r.DayChange == null && r.DayChangePct == null);
        var before = await Client.SummaryAsync("", Ct);
        before.DayChangeExcluded.ShouldBe(rows.Count(r => r.DayChange is null));
        before.DayChangeExcluded.ShouldBeGreaterThanOrEqualTo(2);

        await QuoteAsync(MondayMorning.AddMinutes(1), new HoldingQuote(_alpha, 63m, 59.5m)); // the first quote of the day: older than the tick, so only the close is taken

        var after = (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Items;
        after.Where(r => r.Symbol == "ALPHA").ShouldAllBe(r => r.PriceSource == "live" && r.LastPrice == 66m && r.PreviousClose == 59.5m && r.DayChange == 650m);
        var summary = await Client.SummaryAsync("", Ct);
        summary.DayChangeExcluded.ShouldBe(before.DayChangeExcluded - 2);
        summary.DayChange.ShouldBe(after.Sum(r => r.DayChange ?? 0m));
    }

    [Fact]
    public async Task The_held_instruments_are_those_of_the_open_holdings_of_the_active_accounts()
    {
        await TwoAccountsAsync();
        using var scope = Factory.Services.CreateScope();

        var held = await scope.ServiceProvider.GetRequiredService<ILivePrices>().ListHeldInstrumentsAsync(Ct);

        held.OrderBy(i => i).ShouldBe(new[] { _alpha, _beta }.OrderBy(i => i));
    }

    // ---- criterion 10/12: the precedence of sync, quote and live; the figures follow the tick ----

    [Fact]
    public async Task A_live_tick_moves_value_pnl_day_change_and_the_summary_consistently()
    {
        await TwoAccountsAsync();
        Dhan.Quotes["1001"] = (63m, 60m);
        Dhan.Quotes["1002"] = (171m, 180m);
        (await Factory.RunNowAsync(Client, "holdings-quotes", Ct)).Status.ShouldBe("succeeded");
        var quoted = await Client.SummaryAsync("", Ct);

        (await LiveAsync(Tick(_alpha, 66m, MondayMorning.AddMinutes(5)))).ShouldBe(2);

        var rows = (await Client.ListHoldingsAsync("?pageSize=100", Ct)).Items;
        var alpha = rows.Where(r => r.Symbol == "ALPHA").ToList();
        alpha.ShouldAllBe(r => r.LastPrice == 66m && r.PreviousClose == 60m && r.PriceSource == "live" && r.DayChange == 600m && r.DayChangePct == 10m);
        var summary = await Client.SummaryAsync("", Ct);
        summary.DayChange.ShouldBe(rows.Sum(r => r.DayChange ?? 0m));
        (summary.DayChange - quoted.DayChange).ShouldBe(600m); // 2 x 100 x (66 - 63), the calculators unchanged
        (summary.Returns.Value - quoted.Returns.Value, summary.Returns.Pnl - quoted.Returns.Pnl).ShouldBe((600m, 600m));
        summary.Returns.Xirr.ShouldNotBe(quoted.Returns.Xirr);
    }

    [Fact]
    public async Task A_sync_on_the_same_india_day_keeps_the_live_price_and_a_sync_on_a_later_day_takes_the_synced_one()
    {
        var (one, _) = await TwoAccountsAsync();
        await LiveAsync(Tick(_alpha, 66m, MondayMorning.AddMinutes(5)));
        Dhan.Holdings[0] = Holding("ALPHA", 100, 50m, 61m, "1001", "INE000A01011");

        (await Client.SyncAsync(one.Id, Ct)).Status.ShouldBe("succeeded");

        var same = await RowAsync(one.Id, "ALPHA");
        (same.LastPrice, same.PriceSource, same.PreviousClose).ShouldBe((66m, "live", null));

        var tuesday = MondayMorning.AddHours(14.5); // Tuesday 00:30 India time
        Factory.Clock.Pin(tuesday);
        var next = await Factory.SignInElevatedAsync(Ct);
        (await next.Client.SyncAsync(one.Id, Ct)).Status.ShouldBe("succeeded");
        var later = (await next.Client.ListHoldingsAsync($"?accountId={one.Id}&q=ALPHA", Ct)).Items.Single();
        (later.LastPrice, later.PriceSource).ShouldBe((61m, "sync"));
    }

    [Fact]
    public async Task A_quote_fetched_before_a_live_tick_keeps_the_live_price_and_still_gives_the_previous_close()
    {
        var (one, _) = await TwoAccountsAsync();
        var tick = MondayMorning.AddMinutes(5);
        await LiveAsync(Tick(_alpha, 66m, tick));

        var older = await QuoteAsync(tick.AddSeconds(-30), new HoldingQuote(_alpha, 63m, 59m));

        older.ShouldBe(2);
        var row = await RowAsync(one.Id, "ALPHA");
        (row.LastPrice, row.PriceSource, row.PriceAt, row.PreviousClose).ShouldBe((66m, "live", tick, 59m));

        await QuoteAsync(tick.AddSeconds(30), new HoldingQuote(_alpha, 67m, null)); // fetched after the tick: the quote takes the price over
        var after = await RowAsync(one.Id, "ALPHA");
        (after.LastPrice, after.PriceSource, after.PreviousClose).ShouldBe((67m, "quote", 59m));
    }

    [Fact]
    public async Task A_live_tick_older_than_a_quote_keeps_the_quote_and_a_newer_one_replaces_it_without_touching_the_previous_close()
    {
        var (one, _) = await TwoAccountsAsync();
        var fetched = MondayMorning.AddMinutes(5);
        await QuoteAsync(fetched, new HoldingQuote(_alpha, 63m, 60m));

        (await LiveAsync(Tick(_alpha, 70m, fetched.AddSeconds(-1)))).ShouldBe(0);
        (await RowAsync(one.Id, "ALPHA")).PriceSource.ShouldBe("quote");

        (await LiveAsync(Tick(_alpha, 70m, fetched.AddSeconds(1)))).ShouldBe(2);
        var row = await RowAsync(one.Id, "ALPHA");
        (row.LastPrice, row.PriceSource, row.PreviousClose).ShouldBe((70m, "live", 60m));
    }

    // ---- criterion 6: the pre-open rule in SQL ----

    [Fact]
    public async Task A_tick_before_09_15_never_replaces_a_quote_of_the_same_india_day_but_one_from_09_15_does()
    {
        var (one, _) = await TwoAccountsAsync();
        await QuoteAsync(India(9, 3), new HoldingQuote(_alpha, 63m, 60m)); // a manual quote run in the pre-open

        (await LiveAsync(Tick(_alpha, 70m, India(9, 8)))).ShouldBe(0);
        (await LiveAsync(Tick(_alpha, 70m, India(9, 14, 59)))).ShouldBe(0);
        (await RowAsync(one.Id, "ALPHA")).PriceSource.ShouldBe("quote");

        (await LiveAsync(Tick(_alpha, 71m, India(9, 15)))).ShouldBe(2);
        (await RowAsync(one.Id, "ALPHA")).PriceSource.ShouldBe("live");
    }

    [Fact]
    public async Task A_pre_open_tick_is_written_over_a_synced_price_a_live_price_and_a_quote_of_an_earlier_day()
    {
        var (one, _) = await TwoAccountsAsync();
        await QuoteAsync(India(15, 30, day: 9), new HoldingQuote(_beta, 170m, 169m)); // Friday's quote
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("UPDATE portfolio.holdings SET last_price_at = {0} WHERE trading_symbol = 'ALPHA'", [India(15, 30, day: 9)], Ct)); // synced on Friday

        (await LiveAsync(Tick(_alpha, 61m, India(9, 5)), Tick(_beta, 172m, India(9, 5)))).ShouldBe(3);
        (await LiveAsync(Tick(_alpha, 62m, India(9, 6)))).ShouldBe(2); // a live price is replaced by a newer indicative one

        var alpha = await RowAsync(one.Id, "ALPHA");
        var beta = await RowAsync(one.Id, "BETA");
        (alpha.LastPrice, alpha.PriceSource, beta.LastPrice, beta.PriceSource, beta.PreviousClose).ShouldBe((62m, "live", 172m, "live", 169m));
    }

    // ---- criterion 10: a row a sync holds ----

    [Fact]
    public async Task A_row_that_a_sync_holds_is_left_out_without_waiting_and_written_by_the_next_flush()
    {
        var (one, _) = await TwoAccountsAsync();
        var tick = Tick(_alpha, 63m, MondayMorning.AddMinutes(5));
        await using var locker = new NpgsqlConnection(Factory.AdminConnectionString);
        await locker.OpenAsync(Ct);
        await using var transaction = await locker.BeginTransactionAsync(Ct);
        await using (var lockRow = new NpgsqlCommand($"SELECT id FROM portfolio.holdings WHERE account_id = '{one.Id}' AND trading_symbol = 'ALPHA' FOR UPDATE", locker, transaction))
        {
            await lockRow.ExecuteScalarAsync(Ct);
        }

        var duringSync = await LiveAsync(tick);

        duringSync.ShouldBe(1); // the other account's row; this one is not waited for
        Factory.Logs.All.ShouldContain(l => l.Contains("feed.price_dropped", StringComparison.Ordinal) && l.Contains("rows=1", StringComparison.Ordinal));
        await transaction.RollbackAsync(Ct);

        (await LiveAsync(tick)).ShouldBe(1);
        (await RowAsync(one.Id, "ALPHA")).PriceSource.ShouldBe("live");
    }

    // ---- criterion 11: market.live_quotes ----

    [Fact]
    public async Task Live_quotes_are_one_row_per_instrument_the_newest_tick_wins_and_a_clear_empties_the_table()
    {
        await TwoAccountsAsync();
        var nifty = await Factory.SeedInstrumentAsync("NIFTY50", "INE000N50001", "13", "Nifty 50", Ct);
        using var scope = Factory.Services.CreateScope();
        var quotes = scope.ServiceProvider.GetRequiredService<ILiveQuotes>();
        var at = MondayMorning.AddMinutes(5);

        (await quotes.UpsertAsync([Tick(nifty, 24000.12345m, at), Tick(_alpha, 63m, at)], Ct)).ShouldBe(2);
        (await quotes.UpsertAsync([Tick(nifty, 24100m, at.AddSeconds(2))], Ct)).ShouldBe(1);
        (await quotes.UpsertAsync([Tick(nifty, 1m, at.AddSeconds(1))], Ct)).ShouldBe(0); // late: changes nothing
        (await quotes.UpsertAsync([Tick(nifty, 24100m, at.AddSeconds(2))], Ct)).ShouldBe(0); // the same tick again
        (await quotes.UpsertAsync([Tick(nifty, 24100.5m, at.AddSeconds(2))], Ct)).ShouldBe(1); // the same second, another price: a new tick
        (await quotes.UpsertAsync([Tick(nifty, 24100m, at.AddSeconds(2))], Ct)).ShouldBe(1);

        (await ScalarAsync<long>("SELECT count(*) FROM market.live_quotes")).ShouldBe(2);
        (await ScalarAsync<decimal>($"SELECT last_price FROM market.live_quotes WHERE instrument_id = '{nifty}'")).ShouldBe(24100m);
        (await ScalarAsync<DateTime>($"SELECT tick_at AT TIME ZONE 'UTC' FROM market.live_quotes WHERE instrument_id = '{nifty}'")).ShouldBe(at.AddSeconds(2).UtcDateTime);

        await quotes.ClearAsync(Ct);

        (await ScalarAsync<long>("SELECT count(*) FROM market.live_quotes")).ShouldBe(0);
    }

    [Fact]
    public async Task The_migration_made_live_quotes_unlogged_and_the_database_takes_live_as_a_price_source_and_refuses_another()
    {
        await TwoAccountsAsync();

        (await ScalarAsync<string>("SELECT relpersistence::text FROM pg_class WHERE oid = 'market.live_quotes'::regclass")).ShouldBe("u");
        (await ScalarAsync<string>("SELECT data_type || '/' || numeric_precision || '/' || numeric_scale FROM information_schema.columns WHERE table_schema = 'market' AND table_name = 'live_quotes' AND column_name = 'last_price'")).ShouldBe("numeric/18/4");
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("UPDATE portfolio.holdings SET last_price_source = 'live'", Ct));
        var error = await Should.ThrowAsync<PostgresException>(async () => await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("UPDATE portfolio.holdings SET last_price_source = 'other'", Ct)));
        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    // ---- criteria 13 and 14: the flush, the alerts and the notification ----

    [Fact]
    public async Task A_flush_writes_the_holdings_and_live_quotes_triggers_the_alert_once_with_the_live_actor_and_notifies()
    {
        await TwoAccountsAsync();
        var nifty = await Factory.SeedInstrumentAsync("NIFTY50", "INE000N50001", "13", "Nifty 50", Ct);
        var alertId = Guid.NewGuid();
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES ({0}, {1}, 'at_or_above', 24050, 'active', now(), 'seed')", [alertId, nifty], Ct));
        var flush = Factory.Services.GetRequiredService<ILiveFlush>();
        var tick = MondayMorning.AddMinutes(1);
        using var received = new SemaphoreSlim(0);
        await using var listener = new NpgsqlConnection(Factory.AdminConnectionString);
        await listener.OpenAsync(Ct);
        listener.Notification += (_, e) =>
        {
            if (e.Channel == "live_prices")
            {
                received.Release();
            }
        };
        await using (var listen = new NpgsqlCommand("LISTEN live_prices", listener))
        {
            await listen.ExecuteNonQueryAsync(Ct);
        }

        // The connection comes from the pool and can carry a live_prices notification that an earlier test left unread: read what is pending and count from zero, so that only this flush is counted.
        await using (var ping = new NpgsqlCommand("SELECT 1", listener))
        {
            await ping.ExecuteScalarAsync(Ct);
        }

        while (received.Wait(0, Ct))
        {
        }

        flush.Add(Tick(_alpha, 65m, tick));
        flush.Add(Tick(nifty, 24100m, tick));
        var result = await flush.FlushAsync(Ct);

        (result.Ticks, result.HoldingsUpdated, result.QuotesWritten, result.AlertsTriggered, result.Notified, result.Failed).ShouldBe((2, 2, 1, 1, true, false));
        (await ScalarAsync<decimal>($"SELECT last_price FROM market.live_quotes WHERE instrument_id = '{nifty}'")).ShouldBe(24100m);
        (await ScalarAsync<string>($"SELECT status || '/' || trigger_price::text FROM portfolio.price_alerts WHERE id = '{alertId}'")).ShouldBe("triggered/24100.0000");
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'PriceAlertTriggered' AND actor = 'system:live-feed'")).ShouldBe(1);
        await listener.WaitAsync(TimeSpan.FromSeconds(10), Ct); // delivers the notification to the handler
        received.CurrentCount.ShouldBe(1);

        flush.Add(Tick(nifty, 24200m, tick.AddSeconds(3)));
        (await flush.FlushAsync(Ct)).AlertsTriggered.ShouldBe(0); // once only
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'PriceAlertTriggered'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_flush_with_nothing_new_sends_no_statement_and_no_notification()
    {
        await TwoAccountsAsync();
        var flush = Factory.Services.GetRequiredService<ILiveFlush>();

        var result = await flush.FlushAsync(Ct);

        result.ShouldBe(LiveFlushResult.Nothing());
    }

    // ---- the heartbeat ----

    [Fact]
    public async Task The_feed_heartbeat_details_round_trip_through_the_heartbeat_row_and_the_workers_row_is_untouched()
    {
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.service_heartbeats WHERE service_name = 'xd-feed'", Ct));
        var details = new FeedHeartbeat(true, 42, MondayMorning, 12.5, 1, 3, 17);
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFeedHeartbeats>().ReportAsync("connected", details, "feed-1", "0.1.0", Ct);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var status = await scope.ServiceProvider.GetRequiredService<IFeedHeartbeats>().GetAsync(Ct);

            status.ShouldNotBeNull();
            (status.State, status.IsAlive, status.Details).ShouldBe(("connected", true, details));
        }

        (await ScalarAsync<string>("SELECT details->>'subscribed' || '/' || (details->>'ticksPerSecond') || '/' || state FROM platform.service_heartbeats WHERE service_name = 'xd-feed'")).ShouldBe("42/12.5/connected");
        (await ScalarAsync<long>("SELECT count(*) FROM platform.service_heartbeats WHERE service_name <> 'xd-feed' AND (state IS NOT NULL OR details IS NOT NULL)")).ShouldBe(0);
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.service_heartbeats WHERE service_name = 'xd-feed'", Ct));
    }

    // ---- the runtime role ----

    [Fact]
    public async Task The_app_role_can_upsert_live_quotes_write_heartbeat_details_and_price_holdings_but_only_the_function_can_empty_the_table()
    {
        await AppRoleProbe.RunAsync(
            Factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, """
                    INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
                    VALUES ('00000000-0000-7000-8000-00000000a001', 'NSE:PROBE', 'NSE', 'NSE_EQ', 'share', 'PROBE', 'Probe', 1, 'PROBE', false, now(), now())
                    """, Ct);
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO market.live_quotes (instrument_id, last_price, tick_at) VALUES ('00000000-0000-7000-8000-00000000a001', 100.5, now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE market.live_quotes SET last_price = 101 WHERE instrument_id = '00000000-0000-7000-8000-00000000a001'", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM market.live_quotes", Ct)).ShouldBe(1L);

                await Refused(connection, "DELETE FROM market.live_quotes");
                await Refused(connection, "TRUNCATE market.live_quotes");

                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO platform.service_heartbeats (service_name, instance, version, started_at, last_seen_at, state, details) VALUES ('xd-feed', 'i', 'v', now(), now(), 'connected', '{\"subscribed\":1}')", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE platform.service_heartbeats SET state = 'idle', details = '{\"subscribed\":0}', last_seen_at = now() WHERE service_name = 'xd-feed'", Ct);

                (await AppRoleProbe.ScalarAsync(connection, "SELECT market.clear_live_quotes()::text", Ct)).ShouldBe(string.Empty);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM market.live_quotes", Ct)).ShouldBe(0L);
            },
            Ct);
    }

    [Fact]
    public async Task The_clear_function_is_not_callable_by_other_roles()
    {
        await AppRoleProbe.RunAsync(
            Factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, "RESET ROLE", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_other_probe') THEN CREATE ROLE xd_other_probe NOLOGIN; END IF; END $$", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "SET ROLE xd_other_probe", Ct);

                await Refused(connection, "SELECT market.clear_live_quotes()");

                await AppRoleProbe.ExecuteAsync(connection, "RESET ROLE", Ct);
            },
            Ct);
    }

    private static async Task Refused(NpgsqlConnection connection, string sql)
    {
        var error = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, sql, Ct));
        error.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task An_alert_hit_at_the_same_moment_by_the_live_flush_and_the_five_minute_check_triggers_exactly_once_with_one_audit_event()
    {
        await TwoAccountsAsync();
        var flush = Factory.Services.GetRequiredService<ILiveFlush>();
        const int rounds = 12;

        for (var round = 1; round <= rounds; round++)
        {
            var alertId = Guid.NewGuid();
            await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES ({0}, {1}, 'at_or_above', 70, 'active', now(), 'seed')", [alertId, _alpha], Ct));
            var at = MondayMorning.AddMinutes(round);
            var price = 70m + round;
            using var gate = new Barrier(2);

            var live = Task.Run(async () =>
            {
                flush.Add(Tick(_alpha, price, at));
                gate.SignalAndWait(Ct);
                return (await flush.FlushAsync(Ct)).AlertsTriggered;
            }, Ct);
            var poll = Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var checker = scope.ServiceProvider.GetRequiredService<PriceAlertChecker>();
                var active = await checker.ListActiveAsync(Ct);
                gate.SignalAndWait(Ct);
                return await checker.CheckAsync(active, new Dictionary<Guid, decimal> { [_alpha] = price }, at, Ct);
            }, Ct);
            var triggered = await Task.WhenAll(live, poll);

            triggered.Sum().ShouldBe(1, $"round {round}: the alert triggers once, whichever path wins");
            (await ScalarAsync<string>($"SELECT status FROM portfolio.price_alerts WHERE id = '{alertId}'")).ShouldBe("triggered");
            (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'PriceAlertTriggered'")).ShouldBe(round, $"round {round}: one audit event per alert");
        }
    }
}
