using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Holdings;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live.Push;

namespace XDrishti.Api.IntegrationTests.Live;

/// <summary>
/// The SignalR hub over the real database (ADR 0024 §7, P2-10a): what <c>Subscribe</c> follows, that only those prices are pushed, that a flushed live price reaches the screen through <c>NOTIFY live_prices</c>, the bounds of a message,
/// the feed status, the end of a session and a listener that lost its database connection. Clock: Monday 2026-10-12 10:00 India time. ALPHA is held in account one, BETA in account two.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveHubPushTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private Guid _alpha;
    private Guid _beta;
    private Guid _one;
    private Guid _two;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Factory.ResetServicesAsync();
        Factory.Services.GetRequiredService<ILiveFlush>().Reset();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.service_heartbeats WHERE service_name = 'xd-feed'", Ct);
            return 0;
        });
        _alpha = await Factory.SeedInstrumentAsync("ALPHA", "INE000A01011", "1001", "Alpha Industries", Ct);
        _beta = await Factory.SeedInstrumentAsync("BETA", "INE000B01012", "1002", "Beta Limited", Ct);
        var one = await ConnectedAccountAsync("Example One", "1000000001");
        var two = await ConnectedAccountAsync("Example Two", "1000000002");
        (_one, _two) = (one.Id, two.Id);
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 60m, "1001", "INE000A01011"));
        await ImportAllAsync(_one, "2024-10-12");
        Dhan.Holdings.Clear();
        Dhan.Holdings.Add(Holding("BETA", 10, 200m, 180m, "1002", "INE000B01012"));
        await ImportAllAsync(_two, "2024-10-12");
    }

    private static CancellationToken Timeout()
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        source.CancelAfter(Wait);
        return source.Token;
    }

    private async Task<Guid> HoldingIdAsync(string symbol) =>
        (await Client.ListHoldingsAsync($"?q={symbol}", Ct)).Items.Single().Id;

    private static IEnumerable<JsonElement> Instruments(IEnumerable<HubMessage> messages) =>
        messages.SelectMany(m => m.Argument.GetProperty("instruments").EnumerateArray());

    private static HashSet<Guid> Ids(IEnumerable<HubMessage> messages) =>
        [.. Instruments(messages).Select(i => i.GetProperty("instrumentId").GetGuid())];

    /// <summary>
    /// Writes a live price the way the feed does - through the price sink - which notifies the API. The sink sends at most one notification a second and holds one back until a later flush (the feed flushes every
    /// two seconds), so a flush that was held back is repeated, without ticks, until the notification went out.
    /// </summary>
    private async Task FlushTickAsync(Guid instrument, decimal price, int secondsAfterMorning)
    {
        var flush = Factory.Services.GetRequiredService<ILiveFlush>();
        flush.Add(new LivePrice(instrument, price, MondayMorning.AddSeconds(secondsAfterMorning)));
        var result = await flush.FlushAsync(Ct);
        result.Failed.ShouldBeFalse();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!result.Notified && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Yield();
            result = await flush.FlushAsync(Ct);
        }

        result.Notified.ShouldBeTrue();
    }

    // ---- Subscribe: the scope ----

    [Fact]
    public async Task Subscribe_to_an_account_follows_its_instruments_and_sends_their_current_prices_first()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);

        var ack = await hub.InvokeAsync("Subscribe", [new { accountId = _one, bucketId = (Guid?)null, holdingIds = Array.Empty<Guid>() }], Timeout());
        var first = await hub.NextEventAsync("pricesChanged", Timeout());

        ack.GetProperty("subscribed").GetInt32().ShouldBe(1);
        ack.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        ack.GetProperty("failed").GetBoolean().ShouldBeFalse();
        var price = first.Argument.GetProperty("instruments").EnumerateArray().Single();
        price.GetProperty("instrumentId").GetGuid().ShouldBe(_alpha);
        price.GetProperty("lastPrice").GetDecimal().ShouldBe(60m);
        price.GetProperty("source").GetString().ShouldBe("sync");
        price.TryGetProperty("priceAt", out var priceAt).ShouldBeTrue();
        DateTimeOffset.Parse(priceAt.GetString()!, CultureInfo.InvariantCulture).ShouldBe(MondayMorning, TimeSpan.FromMinutes(5));
        first.Argument.TryGetProperty("asOf", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_an_account_the_scope_is_every_account_that_counts_in_totals()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);

        var ack = await hub.InvokeAsync("Subscribe", [new { }], Timeout());
        var messages = await hub.CollectEventsAsync("pricesChanged", got => Ids(got).Count >= 2, Timeout());

        ack.GetProperty("subscribed").GetInt32().ShouldBe(2);
        Ids(messages).ShouldBe([_alpha, _beta], ignoreOrder: true);
    }

    [Fact]
    public async Task The_holdings_the_screen_shows_are_added_to_the_scope()
    {
        var beta = await HoldingIdAsync("BETA");
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);

        var ack = await hub.InvokeAsync("Subscribe", [new { accountId = _one, holdingIds = new[] { beta, Guid.NewGuid() } }], Timeout());
        var messages = await hub.CollectEventsAsync("pricesChanged", got => Ids(got).Count >= 2, Timeout());

        ack.GetProperty("subscribed").GetInt32().ShouldBe(2); // an unknown holding id adds nothing
        Ids(messages).ShouldBe([_alpha, _beta], ignoreOrder: true);
    }

    [Fact]
    public async Task A_scope_that_matches_nothing_follows_nothing_and_sends_no_prices()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var empty = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await using var control = await HubSocket.ConnectAsync(Factory, owner, Ct);
        var ack = await empty.InvokeAsync("Subscribe", [new { accountId = Guid.NewGuid() }], Timeout());
        await control.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());
        await control.NextEventAsync("pricesChanged", Timeout());

        await FlushTickAsync(_alpha, 61m, 30);
        await control.NextAsync(m => m.Target == "pricesChanged" && Instruments([m]).Any(i => i.GetProperty("lastPrice").GetDecimal() == 61m), Timeout()); // the push that would have reached it has run
        await empty.InvokeAsync("Unsubscribe", [], Timeout());
        await empty.CloseAndDrainAsync(Ct);

        ack.GetProperty("subscribed").GetInt32().ShouldBe(0);
        empty.Seen.ShouldNotContain(m => m.Target == "pricesChanged");
    }

    // ---- what is pushed ----

    [Fact]
    public async Task A_live_price_written_by_the_feed_reaches_the_subscribed_screen_and_not_a_screen_that_did_not_ask_for_it()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var watching = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await using var other = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await watching.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());
        await other.InvokeAsync("Subscribe", [new { accountId = _two }], Timeout());
        await watching.NextEventAsync("pricesChanged", Timeout()); // the current price first
        await other.NextEventAsync("pricesChanged", Timeout());

        await FlushTickAsync(_alpha, 63.5m, 30);
        var pushed = await watching.NextEventAsync("pricesChanged", Timeout());

        var price = pushed.Argument.GetProperty("instruments").EnumerateArray().Single();
        price.GetProperty("instrumentId").GetGuid().ShouldBe(_alpha);
        price.GetProperty("lastPrice").GetDecimal().ShouldBe(63.5m);
        price.GetProperty("source").GetString().ShouldBe("live");
        DateTimeOffset.Parse(price.GetProperty("priceAt").GetString()!, CultureInfo.InvariantCulture).ShouldBe(MondayMorning.AddSeconds(30));
        (await ApiPriceAsync("ALPHA")).ShouldBe(63.5m); // the screen's next read agrees

        await FlushTickAsync(_beta, 181m, 31); // its own tick is the next thing the other screen gets: nothing of ALPHA came before
        var otherPush = await other.NextEventAsync("pricesChanged", Timeout());
        Ids([otherPush]).ShouldBe([_beta]);
    }

    private async Task<decimal> ApiPriceAsync(string symbol) =>
        (await Client.ListHoldingsAsync($"?q={symbol}", Ct)).Items.Single().LastPrice;

    [Fact]
    public async Task Of_several_ticks_before_a_flush_only_the_latest_price_is_pushed()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await hub.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());
        await hub.NextEventAsync("pricesChanged", Timeout());
        var flush = Factory.Services.GetRequiredService<ILiveFlush>();
        for (var i = 1; i <= 5; i++)
        {
            flush.Add(new LivePrice(_alpha, 60m + i, MondayMorning.AddSeconds(10 + i)));
        }

        (await flush.FlushAsync(Ct)).Notified.ShouldBeTrue();
        var pushed = await hub.NextEventAsync("pricesChanged", Timeout());

        Instruments([pushed]).Single().GetProperty("lastPrice").GetDecimal().ShouldBe(65m);
    }

    [Fact]
    public async Task After_unsubscribe_no_prices_are_pushed_and_a_new_subscribe_starts_again()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await hub.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());
        await hub.NextEventAsync("pricesChanged", Timeout());

        await hub.InvokeAsync("Unsubscribe", [], Timeout());
        await FlushTickAsync(_alpha, 64m, 30);
        await hub.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout()); // the same screen again
        var again = await hub.NextEventAsync("pricesChanged", Timeout());

        Instruments([again]).Single().GetProperty("lastPrice").GetDecimal().ShouldBe(64m); // the unsubscribed time produced no message of its own
    }

    [Fact]
    public async Task A_quote_written_without_any_notification_is_not_missed_when_the_scope_is_subscribed_again()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE portfolio.holdings SET last_price = 70, last_price_at = {0}, last_price_source = 'quote' WHERE trading_symbol = 'ALPHA'", [MondayMorning.AddSeconds(5)], Ct);
            return 0;
        });

        await hub.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());
        var message = await hub.NextEventAsync("pricesChanged", Timeout());

        var price = Instruments([message]).Single();
        (price.GetProperty("lastPrice").GetDecimal(), price.GetProperty("source").GetString()).ShouldBe((70m, "quote"));
    }

    [Fact]
    public async Task The_price_of_an_instrument_that_is_not_a_holding_comes_from_the_live_quotes_table()
    {
        // an instrument followed through market.live_quotes is only reachable through a holding scope today; the reader serves it all the same
        var index = Guid.CreateVersion7();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
                VALUES ({0}, 'NSE:NIFTY', 'NSE', 'IDX_I', 'index', 'Nifty 50', 'Nifty 50', 1, 'NSE:NIFTY', false, now(), now());
                INSERT INTO market.live_quotes (instrument_id, last_price, tick_at) VALUES ({0}, 24100, {1});
                """,
                [index, MondayMorning.AddSeconds(3)],
                Ct);
            return 0;
        });
        using var scope = Factory.Services.CreateScope();

        var read = await scope.ServiceProvider.GetRequiredService<ILivePriceReader>().ReadAsync([index, _alpha, Guid.NewGuid()], Ct);

        read.Select(p => (p.InstrumentId, p.LastPrice, p.Source)).ShouldBe([.. new[] { (index, 24100m, "live"), (_alpha, 60m, "sync") }.OrderBy(x => x.Item1)]);
    }

    // ---- bounds on the wire ----

    [Fact]
    public async Task A_large_scope_is_sent_in_messages_of_at_most_500_instruments_and_32_KB()
    {
        const int Count = 400;
        var three = (await ConnectedAccountAsync("Example Three", "1000000003")).Id;
        Dhan.Holdings.Clear();
        for (var i = 0; i < Count; i++)
        {
            var symbol = string.Create(CultureInfo.InvariantCulture, $"SYM{i:D4}");
            var isin = string.Create(CultureInfo.InvariantCulture, $"INE9{i:D5}012");
            await Factory.SeedInstrumentAsync(symbol, isin, (2000 + i).ToString(CultureInfo.InvariantCulture), symbol + " Limited", Ct);
            Dhan.Holdings.Add(Holding(symbol, 10, 100m, 123.4567m + i, (2000 + i).ToString(CultureInfo.InvariantCulture), isin));
        }

        await ImportAllAsync(three, "2024-10-12");
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);

        var ack = await hub.InvokeAsync("Subscribe", [new { accountId = three }], Timeout());
        var messages = await hub.CollectEventsAsync("pricesChanged", got => Ids(got).Count >= Count, Timeout());

        ack.GetProperty("subscribed").GetInt32().ShouldBe(Count);
        messages.Count.ShouldBeGreaterThan(1); // 400 prices do not fit in 32 KB
        messages.ShouldAllBe(m => m.Bytes <= LiveHubContract.MaxMessageBytes);
        messages.ShouldAllBe(m => m.Argument.GetProperty("instruments").GetArrayLength() <= LiveHubContract.MaxInstrumentsPerMessage);
        Instruments(messages).Count().ShouldBe(Count);
    }

    // ---- the feed status ----

    [Fact]
    public async Task The_status_is_pushed_when_the_feeds_state_changes_and_the_rest_endpoint_says_the_same()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        var first = await hub.NextEventAsync("feedStatus", Timeout());
        first.Argument.GetProperty("state").GetString().ShouldBe("not_running"); // no heartbeat yet

        var tick = MondayMorning.AddSeconds(-3);
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFeedHeartbeats>().ReportAsync("connected", new FeedHeartbeat(true, 42, tick, 7.5, 0, 0, 3), "test", "1", Ct);
        }

        var changed = await hub.NextAsync(m => m.Target == "feedStatus" && m.Argument.GetProperty("state").GetString() == "connected", Timeout());
        using var response = await Client.GetAsync("/api/live/status", Ct);
        var rest = await response.ReadAsync<LiveFeedStatus>(Ct);

        changed.Argument.GetProperty("subscribed").GetInt32().ShouldBe(42);
        DateTimeOffset.Parse(changed.Argument.GetProperty("lastTickAt").GetString()!, CultureInfo.InvariantCulture).ShouldBe(tick);
        rest.ShouldBe(new LiveFeedStatus("connected", 42, tick));
    }

    [Fact]
    public async Task The_status_endpoint_reports_a_feed_that_never_reported_as_not_running()
    {
        using var response = await Client.GetAsync("/api/live/status", Ct);

        (await response.ReadAsync<LiveFeedStatus>(Ct)).ShouldBe(new LiveFeedStatus("not_running", 0, null));
    }

    // ---- sessions ----

    [Fact]
    public async Task A_screen_is_disconnected_when_its_session_is_logged_out()
    {
        var owner = await Factory.SignInAsync(Ct);
        var other = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await using var stays = await HubSocket.ConnectAsync(Factory, other, Ct);
        await hub.NextEventAsync("feedStatus", Timeout());

        using var logout = await owner.Client.PostAsync("/api/auth/logout", Ct);

        logout.IsSuccessStatusCode.ShouldBeTrue();
        (await hub.WaitForCloseAsync(Timeout())).ShouldBeTrue();
        await stays.InvokeAsync("Unsubscribe", [], Timeout()); // the other session's connection still answers
    }

    [Fact]
    public async Task A_screen_is_disconnected_when_its_session_idles_out_even_though_the_connection_itself_is_busy()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await hub.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());

        Factory.Clock.Advance(TimeSpan.FromMinutes(31)); // 30 minutes without a request of the session

        (await hub.WaitForCloseAsync(Timeout())).ShouldBeTrue();
    }

    // ---- the listener ----

    [Fact]
    public async Task The_listener_listens_again_by_itself_after_its_database_connection_was_cut()
    {
        var owner = await Factory.SignInAsync(Ct);
        await using var hub = await HubSocket.ConnectAsync(Factory, owner, Ct);
        await hub.InvokeAsync("Subscribe", [new { accountId = _one }], Timeout());
        await hub.NextEventAsync("pricesChanged", Timeout());
        await FlushTickAsync(_alpha, 62m, 20); // it listens: a push proves it
        await hub.NextEventAsync("pricesChanged", Timeout());

        var cut = await Factory.WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE query = 'LISTEN live_prices' AND pid <> pg_backend_pid()";
            return Convert.ToInt32(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
        });
        await FlushTickAsync(_alpha, 66m, 40); // sent while nobody listens, or just as it is listening again
        var pushed = await hub.NextAsync(m => m.Target == "pricesChanged" && Instruments([m]).Any(i => i.GetProperty("lastPrice").GetDecimal() == 66m), Timeout());

        cut.ShouldBeGreaterThanOrEqualTo(1);
        pushed.ShouldNotBeNull();
    }
}
