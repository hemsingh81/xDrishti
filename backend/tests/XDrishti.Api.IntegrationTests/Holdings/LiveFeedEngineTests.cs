using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Platform.SystemStatus;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The feed's own side on the real TimescaleDB (ADR 0024, P2-10 stage 2): the holdings and indices the active set is built from, the manual-run marker, <c>LISTEN active_set_changed</c>, and the whole feed - supervisor, window, run record,
/// heartbeat, connection manager and the price sink - over the database with the Dhan socket replaced. Clock: Monday 2026-10-12 10:00 India time, inside the window.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveFeedEngineTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private static readonly object BothRoles = new { data = new { enabled = true, kind = "primary" }, portfolio = new { enabled = true } };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
        Factory.FeedSocket.Reset();
        Factory.Services.GetRequiredService<ILiveFlush>().Reset();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.service_heartbeats WHERE service_name = 'xd-feed'", Ct);
            return 0;
        });
    }

    private async Task<Guid> IndexAsync(string canonical, string displayName, string? securityId, bool retired = false)
    {
        var id = Guid.CreateVersion7();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at, retired_at)
                VALUES ({0}, {1}, 'NSE', 'IDX_I', 'index', {2}, {2}, 1, {1}, false, now(), now(), CASE WHEN {3} THEN now() ELSE NULL END)
                """,
                [id, canonical, displayName, retired],
                Ct);
            if (securityId is not null)
            {
                await db.Database.ExecuteSqlRawAsync("INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES ({0}, 'dhan', {1}, 'IDX_I')", [id, securityId], Ct);
            }

            return 0;
        });
        return id;
    }

    private async Task<(Guid One, Guid Two)> TwoAccountsHoldingAsync()
    {
        var one = await ConnectedAccountAsync("Example One", "1000000001", roles: BothRoles);
        var two = await ConnectedAccountAsync("Example Two", "1000000002");
        Dhan.Holdings.AddRange(
        [
            Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"),
            Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"),
            Holding("BSEONLY", 7, 10m, 11m, "5555", null, "BSE"),
        ]);
        await ImportAllAsync(one.Id, "2024-10-12");
        await ImportAllAsync(two.Id, "2024-10-12");
        return (one.Id, two.Id);
    }

    private async Task<T> ScalarAsync<T>(string sql) =>
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync(Ct))!;
        });

    // ---- the holdings of the active set ----

    [Fact]
    public async Task Held_securities_add_up_over_accounts_largest_first_and_leave_out_unmapped_retired_and_closed_holdings()
    {
        var alpha = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        var beta = await Factory.SeedInstrumentAsync("BETA", "INE000B01012", "1002", "Beta Limited", Ct);
        await TwoAccountsHoldingAsync();
        using var scope = Factory.Services.CreateScope();
        var prices = scope.ServiceProvider.GetRequiredService<ILivePrices>();

        var held = await prices.ListHeldSecuritiesAsync(Ct);

        held.Select(h => (h.InstrumentId, h.SecurityId, h.Value)).ShouldBe([(alpha, "1001", 12_000m), (beta, "1002", 3_600m)]); // BSEONLY is not on the NSE
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE market.instruments SET retired_at = now() WHERE id = {0}", [beta], Ct);
            return 0;
        });
        (await prices.ListHeldSecuritiesAsync(Ct)).Select(h => h.InstrumentId).ShouldBe([alpha]);
    }

    [Fact]
    public async Task A_security_whose_holdings_are_all_closed_is_not_in_the_list()
    {
        await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        var (one, two) = await TwoAccountsHoldingAsync();
        Dhan.Holdings.RemoveAll(h => h.Symbol == "ALPHA");
        (await Client.SyncAsync(one, Ct)).Status.ShouldBe("succeeded");
        (await Client.SyncAsync(two, Ct)).Status.ShouldBe("succeeded");
        using var scope = Factory.Services.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<ILivePrices>().ListHeldSecuritiesAsync(Ct)).ShouldBeEmpty();
    }

    // ---- the indices of the active set ----

    [Fact]
    public async Task Indices_are_found_by_symbol_then_by_display_name_and_retired_or_unmapped_ones_are_not()
    {
        var nifty = await IndexAsync("NSE:NIFTY", "Nifty 50", "13");
        var bank = await IndexAsync("NSE:NIFTYBANKX", "Nifty Bank", "25"); // another symbol: found by its display name
        await IndexAsync("NSE:INDIAVIX", "India VIX", "21", retired: true);
        await IndexAsync("NSE:NIFTYNEXT50", "Nifty Next 50", null); // no Dhan id
        using var scope = Factory.Services.CreateScope();
        var indices = scope.ServiceProvider.GetRequiredService<IIndexSecurities>();
        IndexName[] wanted = [new("NIFTY 50", "NSE:NIFTY", "Nifty 50"), new("NIFTY BANK", "NSE:BANKNIFTY", "Nifty Bank"), new("INDIA VIX", "NSE:INDIAVIX", "India VIX"), new("NEXT", "NSE:NIFTYNEXT50", "Nifty Next 50")];

        var found = await indices.FindAsync(wanted, Ct);

        found.Select(f => (f.Label, f.InstrumentId, f.SecurityId)).ShouldBe([("NIFTY 50", nifty, "13"), ("NIFTY BANK", bank, "25")]);
        (await indices.FindByIdAsync(nifty, Ct))!.SecurityId.ShouldBe("13");
        (await indices.FindByIdAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_share_is_not_an_index()
    {
        var alpha = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        using var scope = Factory.Services.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<IIndexSecurities>().FindByIdAsync(alpha, Ct)).ShouldBeNull();
    }

    // ---- the manual-run marker ----

    [Fact]
    public async Task The_manual_run_marker_is_read_from_the_settings_and_a_missing_or_broken_one_means_none()
    {
        using var scope = Factory.Services.CreateScope();
        var manual = scope.ServiceProvider.GetRequiredService<IFeedManualRun>();
        (await manual.GetManualUntilAsync(Ct)).ShouldBeNull();

        await SetMarkerAsync("2026-10-12T11:30:00Z");
        (await manual.GetManualUntilAsync(Ct)).ShouldBe(new DateTimeOffset(2026, 10, 12, 11, 30, 0, TimeSpan.Zero));

        await SetMarkerAsync("soon");
        (await manual.GetManualUntilAsync(Ct)).ShouldBeNull();
    }

    private async Task SetMarkerAsync(string value) =>
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ({0}, {1}, 'test', now()) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value",
                [IFeedManualRun.Key, value],
                Ct);
            return 0;
        });

    // ---- LISTEN / NOTIFY ----

    [Fact]
    public async Task A_notification_that_the_active_set_changed_reaches_the_listener()
    {
        var changes = Factory.Services.GetRequiredService<IActiveSetChanges>();
        using var received = new SemaphoreSlim(0);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var watching = Task.Run(() => changes.WatchAsync(() => received.Release(), stop.Token), Ct);

        // the listener needs a moment to issue its LISTEN: send until it answers
        using var scope = Factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ILiveNotifier>();
        var delivered = false;
        for (var i = 0; i < 50 && !delivered; i++)
        {
            await notifier.ActiveSetChangedAsync(Ct);
            delivered = await received.WaitAsync(TimeSpan.FromMilliseconds(200), Ct);
        }

        await stop.CancelAsync();
        await watching;
        delivered.ShouldBeTrue();
    }

    // ---- the whole feed over the database ----

    [Fact]
    public async Task The_feed_opens_its_run_subscribes_the_active_set_writes_a_live_price_and_closes_its_run_when_stopped()
    {
        await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        await IndexAsync("NSE:NIFTY", "Nifty 50", "13");
        var (one, _) = await TwoAccountsHoldingAsync();
        await using var listener = new NpgsqlConnection(Factory.AdminConnectionString);
        await listener.OpenAsync(Ct);
        using var priced = new SemaphoreSlim(0);
        listener.Notification += (_, e) =>
        {
            if (e.Channel == "live_prices")
            {
                priced.Release();
            }
        };
        await using (var listen = new NpgsqlCommand("LISTEN live_prices", listener))
        {
            await listen.ExecuteNonQueryAsync(Ct);
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var feed = Task.Run(() => Factory.Services.GetRequiredService<IFeedSupervisor>().RunAsync(stop.Token), Ct);
        await Factory.FeedSocket.WaitForSubscriptionsAsync(2, Ct);

        var subscribed = Factory.FeedSocket.Subscribed;
        subscribed.Single(s => s.Mode == FeedMode.Quote).Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 1001)]); // BETA is not in the master list here
        subscribed.Single(s => s.Mode == FeedMode.Ticker).Securities.ShouldBe([new FeedSecurity(FeedSegments.Index, 13)]);
        (await ScalarAsync<long>("SELECT count(*) FROM platform.service_runs WHERE service_id = 'live-feed' AND status = 'running' AND trigger = 'scheduled'")).ShouldBe(1);

        var at = MondayMorning.AddSeconds(30); // newer than the synced price of 10:00:00, and not past the clock by more than a minute
        Factory.FeedSocket.Push(new FeedPacket(FeedPacketKind.Ticker, new FeedSecurity(FeedSegments.NseEquity, 1001), 63.5m, at));
        Factory.FeedSocket.Push(new FeedPacket(FeedPacketKind.Ticker, new FeedSecurity(FeedSegments.Index, 13), 24100m, at));
        (await listener.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldBeTrue("no live_prices notification arrived"); // the connection hands the notification over while it waits
        priced.CurrentCount.ShouldBe(1);

        var row = (await Client.ListHoldingsAsync($"?accountId={one}&q=ALPHA", Ct)).Items.Single();
        (row.LastPrice, row.PriceSource, row.PriceAt).ShouldBe((63.5m, "live", at));
        var niftyId = await ScalarAsync<Guid>("SELECT id FROM market.instruments WHERE canonical_symbol = 'NSE:NIFTY'");
        (await ScalarAsync<decimal>($"SELECT last_price FROM market.live_quotes WHERE instrument_id = '{niftyId}'")).ShouldBe(24100m);
        (await ScalarAsync<string>("SELECT state FROM platform.service_heartbeats WHERE service_name = 'xd-feed'")).ShouldNotBeNullOrEmpty();

        await stop.CancelAsync();
        await feed;

        var run = await ScalarAsync<string>("SELECT status || '/' || message FROM platform.service_runs WHERE service_id = 'live-feed'");
        run.ShouldBe("succeeded/Stopped");
    }

    // ---- the Services screen over the running feed: pause, resume, Run now, status (P2-10 criterion 15) ----

    private LiveFeedSupervisor Supervisor => (LiveFeedSupervisor)Factory.Services.GetRequiredService<IFeedSupervisor>();

    private async Task<ServiceView> FeedServiceAsync()
    {
        using var response = await Client.GetAsync("/api/services/live-feed", Ct);
        return await response.ReadAsync<ServiceView>(Ct);
    }

    private async Task<string> RunsAsync() =>
        await ScalarAsync<string>("SELECT coalesce(string_agg(status || '/' || trigger || '/' || coalesce(message, ''), ' ; ' ORDER BY started_at), '') FROM platform.service_runs WHERE service_id = 'live-feed'");

    /// <summary>ALPHA held in two accounts and the Nifty 50: the feed subscribes two messages (quote mode for the share, ticker mode for the index).</summary>
    private async Task<(Guid One, Guid Two)> SetUpFeedAsync()
    {
        await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        await IndexAsync("NSE:NIFTY", "Nifty 50", "13");
        return await TwoAccountsHoldingAsync();
    }

    /// <summary>Looks at the window until the heartbeat carries the connection: the session starts on the first look and subscribes a moment later.</summary>
    private async Task<ServiceView> LookUntilConnectedAsync()
    {
        for (var i = 0; i < 400; i++)
        {
            await Supervisor.TickAsync(Ct);
            var view = await FeedServiceAsync();
            if (view.Live is { Connected: true, Subscribed: > 0 })
            {
                return view;
            }

            await Task.Yield();
        }

        throw new InvalidOperationException("the feed did not report a connection");
    }

    private async Task StopTheWindowAsync()
    {
        (await Client.PostAsync("/api/services/live-feed/pause", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.app_settings WHERE key = {0}", [IFeedManualRun.Key], Ct));
        await Supervisor.TickAsync(Ct);
    }

    [Fact]
    public async Task Pause_ends_the_window_run_as_paused_and_resume_opens_a_new_one()
    {
        await SetUpFeedAsync();
        await Supervisor.TickAsync(Ct);
        await Factory.FeedSocket.WaitForSubscriptionsAsync(2, Ct);
        (await RunsAsync()).ShouldBe("running/scheduled/");

        using var paused = await Client.PostAsync("/api/services/live-feed/pause", Ct);
        await Supervisor.TickAsync(Ct);

        paused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RunsAsync()).ShouldBe("succeeded/scheduled/Paused");
        (await FeedServiceAsync()).Status.ShouldBe("paused");
        var subscribed = Factory.FeedSocket.Subscribed.Count;

        using var resumed = await Client.PostAsync("/api/services/live-feed/resume", Ct);
        await Supervisor.TickAsync(Ct);
        await Factory.FeedSocket.WaitForSubscriptionsAsync(subscribed + 2, Ct);

        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RunsAsync()).ShouldBe("succeeded/scheduled/Paused ; running/scheduled/");
        (await FeedServiceAsync()).Status.ShouldBe("running");
        await StopTheWindowAsync();
        (await RunsAsync()).ShouldBe("succeeded/scheduled/Paused ; succeeded/scheduled/Paused");
    }

    [Fact]
    public async Task Run_now_on_a_paused_feed_queues_a_manual_run_that_the_feed_opens_and_a_second_request_is_refused()
    {
        await SetUpFeedAsync();
        (await Client.PostAsync("/api/services/live-feed/pause", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Supervisor.TickAsync(Ct);
        (await RunsAsync()).ShouldBeEmpty();

        using var queued = await Client.PostAsync("/api/services/live-feed/run", Ct);

        queued.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = (await queued.Content.ReadFromJsonAsync<ServiceRunView>(Ct))!;
        (run.Status, run.Trigger, run.Id).ShouldBe(("queued", "manual", Guid.Empty));
        var until = DateTimeOffset.Parse(await ScalarAsync<string>($"SELECT value FROM platform.app_settings WHERE key = '{IFeedManualRun.Key}'"), System.Globalization.CultureInfo.InvariantCulture);
        (until - MondayMorning).ShouldBe(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1));
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'ServiceRunRequested' AND detail LIKE 'live-feed%'")).ShouldBe(1);

        await Supervisor.TickAsync(Ct);
        await Factory.FeedSocket.WaitForSubscriptionsAsync(2, Ct);
        using var again = await Client.PostAsync("/api/services/live-feed/run", Ct);

        (await RunsAsync()).ShouldBe("running/manual/");
        (await FeedServiceAsync()).Status.ShouldBe("running");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ReadProblemAsync(Ct)).Code.ShouldBe("services.already_running");

        (await Client.PostAsync("/api/services/live-feed/pause", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // paused already, but the manual run keeps it up: pausing ends that
        await Supervisor.TickAsync(Ct);

        (await RunsAsync()).ShouldBe("succeeded/manual/Paused");
        await StopTheWindowAsync();
    }

    [Fact]
    public async Task The_service_shows_what_the_feed_reports_and_fails_when_it_stops_reporting_inside_the_window()
    {
        await SetUpFeedAsync();

        var running = await LookUntilConnectedAsync();

        (running.Status, running.RunsAs).ShouldBe(("running", "xd-feed"));
        (running.Live!.State, running.Live.Subscribed).ShouldBe(("connected", running.Live.Subscribed));
        running.Live.Summary.ShouldStartWith("Connected · ");

        Factory.Clock.Advance(TimeSpan.FromMinutes(2)); // no look: the heartbeat is two minutes old

        var silent = await FeedServiceAsync();
        (silent.Status, silent.Live!.State, silent.Live.Summary).ShouldBe(("failed", "not_running", "The live feed is not running."));
        await StopTheWindowAsync();
    }

    private async Task<ServiceStatus> FeedRowAsync()
    {
        using var response = await Client.GetAsync("/api/system/status", Ct);
        return (await response.ReadAsync<SystemStatusResponse>(Ct)).Services.Single(s => s.Name == "xd-feed");
    }

    [Fact]
    public async Task The_system_status_lists_the_feed_and_calls_a_deliberate_stop_stopped()
    {
        await SetUpFeedAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var feed = Task.Run(() => Supervisor.RunAsync(stop.Token), Ct);
        await Factory.FeedSocket.WaitForSubscriptionsAsync(2, Ct);

        var up = await FeedRowAsync();

        up.IsAlive.ShouldBeTrue();
        up.State.ShouldNotBeNullOrEmpty();
        up.State.ShouldNotBe("stopped");
        await stop.CancelAsync();
        await feed;
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var after = await FeedRowAsync();
        (after.IsAlive, after.State).ShouldBe((false, "stopped"));
    }

    // ---- NOTIFY active_set_changed (criterion 8) ----

    private async Task<(NpgsqlConnection Connection, SemaphoreSlim Received)> ListenAsync(string channel)
    {
        // not pooled: a pooled connection keeps its LISTEN and hands the notifications it collected meanwhile to the next test that borrows it
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Factory.AdminConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(Ct);
        var received = new SemaphoreSlim(0);
        connection.Notification += (_, e) =>
        {
            if (e.Channel == channel)
            {
                received.Release();
            }
        };
        await using var listen = new NpgsqlCommand($"LISTEN {channel}", connection);
        await listen.ExecuteNonQueryAsync(Ct);
        return (connection, received);
    }

    private static async Task<int> CountAsync(NpgsqlConnection connection, SemaphoreSlim received, CancellationToken cancellationToken)
    {
        await connection.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken); // hands the notifications over
        return received.CurrentCount;
    }

    [Fact]
    public async Task Creating_and_cancelling_a_price_alert_send_active_set_changed()
    {
        var beta = await Factory.SeedInstrumentAsync("BETA", "INE000B01012", "1002", "Beta Limited", Ct);
        var (connection, received) = await ListenAsync("active_set_changed");
        await using var _ = connection;
        using var __ = received;

        using var created = await Client.PostJsonAsync("/api/portfolio/alerts", new { instrumentId = beta, direction = "at_or_above", level = 250m }, Ct);
        var alert = await created.ReadAsync<XDrishti.Application.Portfolio.Alerts.PriceAlertView>(Ct);
        var afterCreate = await CountAsync(connection, received, Ct);
        using var cancelled = await Client.PostAsync($"/api/portfolio/alerts/{alert.Id}/cancel", Ct);
        var afterCancel = await CountAsync(connection, received, Ct);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (afterCreate, afterCancel).ShouldBe((1, 2));
    }

    [Fact]
    public async Task An_alert_that_triggers_and_a_holding_that_is_added_or_closed_send_active_set_changed_but_a_price_write_does_not()
    {
        var (one, _) = await SetUpFeedAsync();
        var (connection, received) = await ListenAsync("active_set_changed");
        await using var _ = connection;
        using var __ = received;
        var alpha = await ScalarAsync<Guid>("SELECT id FROM market.instruments WHERE canonical_symbol LIKE '%ALPHA'");
        var alertId = Guid.NewGuid();
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES ({0}, {1}, 'at_or_above', 70, 'active', now(), 'seed')", [alertId, alpha], Ct));
        var flush = Factory.Services.GetRequiredService<ILiveFlush>();

        flush.Add(new LivePrice(alpha, 65m, MondayMorning.AddSeconds(30)));
        (await flush.FlushAsync(Ct)).AlertsTriggered.ShouldBe(0);
        var afterPrice = await CountAsync(connection, received, Ct);
        flush.Add(new LivePrice(alpha, 71m, MondayMorning.AddSeconds(40)));
        (await flush.FlushAsync(Ct)).AlertsTriggered.ShouldBe(1);
        var afterTrigger = await CountAsync(connection, received, Ct);
        Dhan.Holdings.RemoveAll(h => h.Symbol == "ALPHA");
        (await Client.SyncAsync(one, Ct)).Status.ShouldBe("succeeded"); // ALPHA is closed in that account
        var afterSync = await CountAsync(connection, received, Ct);

        (afterPrice, afterTrigger).ShouldBe((0, 1));
        afterSync.ShouldBeGreaterThan(afterTrigger);
    }

    [Fact]
    public async Task A_running_feed_subscribes_to_a_new_alert_at_once_not_at_the_next_minute()
    {
        await SetUpFeedAsync();
        var beta = await Factory.SeedInstrumentAsync("BETA", "INE000B01012", "1002", "Beta Limited", Ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var feed = Task.Run(() => Supervisor.RunAsync(stop.Token), Ct);
        await Factory.FeedSocket.WaitForSubscriptionsAsync(2, Ct);
        var before = Factory.FeedSocket.Subscribed.Count;

        using var created = await Client.PostJsonAsync("/api/portfolio/alerts", new { instrumentId = beta, direction = "at_or_above", level = 250m }, Ct);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10)); // the minute-by-minute recompute would take up to 60 s
        await Factory.FeedSocket.WaitForSubscriptionsAsync(before + 1, limit.Token);
        Factory.FeedSocket.Subscribed.Skip(before).SelectMany(s => s.Securities).ShouldContain(new FeedSecurity(FeedSegments.NseEquity, 1002));
        await stop.CancelAsync();
        await feed;
    }
}
