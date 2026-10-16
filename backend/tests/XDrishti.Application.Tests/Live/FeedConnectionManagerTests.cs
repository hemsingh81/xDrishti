using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Live;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>The connection state machine over a fake socket and a fake clock (P2-10 criteria 4, 5, 6, 8 and 17).</summary>
public sealed class FeedConnectionManagerTests
{
    private static readonly TimeSpan Flush = TimeSpan.FromSeconds(2);

    private static readonly LiveFeedSettings Quiet = LiveFeedSettings.Default with { SubscribeSpacing = TimeSpan.Zero, FlushInterval = TimeSpan.FromSeconds(7) };

    private static void AddTypicalSet(FeedWorld world)
    {
        world.Provider.Candidates.AddRange([Holding(Reliance, "2885", 5000m), Index(Nifty, "13"), Alert(Infosys, "1594")]);
    }

    // ---- start and subscribe ----

    [Fact]
    public async Task It_connects_at_the_start_and_subscribes_the_whole_set_in_quote_mode_for_holdings_and_ticker_mode_for_the_rest()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);

        await world.StartConnectedAsync();

        var connection = world.Connector.Connection(0).ShouldNotBeNull();
        connection.Subscribed.Single(s => s.Mode == FeedMode.Quote).Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 2885)]);
        connection.Subscribed.Single(s => s.Mode == FeedMode.Ticker).Securities.ShouldBe([new FeedSecurity(FeedSegments.Index, 13), new FeedSecurity(FeedSegments.NseEquity, 1594)], ignoreOrder: true);
        world.Manager.State.ShouldBe(FeedStates.Connected);
        world.Metrics.Snapshot(world.Clock.GetUtcNow()).Subscribed.ShouldBe(3);
        world.Metrics.Snapshot(world.Clock.GetUtcNow()).Connected.ShouldBeTrue();
        await world.StopAsync();
    }

    [Fact]
    public async Task A_window_starts_with_a_clean_slate()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);

        await world.StartConnectedAsync();

        world.Flush.Resets.ShouldBe(1);
        world.Quotes.Clears.ShouldBe(0); // the supervisor empties the table, once per window
        await world.StopAsync();
    }

    [Fact]
    public async Task An_empty_set_connects_subscribes_nothing_and_is_idle()
    {
        using var world = new FeedWorld();

        await world.StartConnectedAsync();

        world.Connector.Connection(0)!.Subscribed.ShouldBeEmpty();
        world.Manager.State.ShouldBe(FeedStates.Idle);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_big_set_is_subscribed_in_messages_of_at_most_100()
    {
        using var world = new FeedWorld(settings: LiveFeedSettings.Default with { SubscribeSpacing = TimeSpan.Zero, MaxInstruments = 1000 });
        world.Provider.Candidates.AddRange(Enumerable.Range(0, 250).Select(i => Holding(Guid.NewGuid(), (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), 1000 - i)));

        await world.StartConnectedAsync();

        world.Connector.Connection(0)!.Subscribed.Select(s => s.Securities.Length).ShouldBe([100, 100, 50]);
        await world.StopAsync();
    }

    // ---- packets to the sink ----

    [Fact]
    public async Task A_tick_of_a_subscribed_instrument_goes_to_the_sink_with_its_own_time()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        var at = world.Clock.GetUtcNow().AddSeconds(-1);

        world.Connector.Connection(0)!.Push(Tick(FeedSegments.NseEquity, 2885, 2950.5m, at));
        await FeedWorld.UntilAsync(() => world.Flush.Added.Count == 1);

        world.Flush.Added[0].ShouldBe(new LivePrice(Reliance, 2950.5m, at));
        world.Metrics.Snapshot(world.Clock.GetUtcNow()).LastTickAt.ShouldBe(at);
        await world.StopAsync();
    }

    [Fact]
    public async Task Prices_before_the_open_are_passed_to_the_sink_to_judge()
    {
        var preOpen = new DateTimeOffset(2026, 10, 12, 9, 5, 0, FeedWorld.Ist);
        using var world = new FeedWorld(start: preOpen);
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        world.Connector.Connection(0)!.Push(Tick(FeedSegments.NseEquity, 2885, 2950.5m, preOpen.AddSeconds(-2)));
        await FeedWorld.UntilAsync(() => world.Flush.Added.Count == 1);

        world.Flush.Added[0].TickAt.ShouldBe(preOpen.AddSeconds(-2));
        await world.StopAsync();
    }

    [Fact]
    public async Task A_tick_of_an_instrument_outside_the_set_is_dropped_and_counted_and_the_previous_close_is_not_used()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        var connection = world.Connector.Connection(0)!;
        var now = world.Clock.GetUtcNow();

        connection.Push(Tick(FeedSegments.NseEquity, 77777, 10m, now));
        connection.Push(Tick(FeedSegments.NseEquity, 2885, 2900m, now, FeedPacketKind.PreviousClose));
        connection.Push(Tick(FeedSegments.NseEquity, 2885, 2951m, now));
        await FeedWorld.UntilAsync(() => world.Flush.Added.Count == 1);

        world.Flush.Added.ShouldHaveSingleItem().Price.ShouldBe(2951m);
        world.Metrics.Dropped.ShouldBe(1);
        await world.StopAsync();
    }

    [Fact]
    public async Task The_sink_is_flushed_every_flush_interval_and_once_more_when_the_session_stops()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        await world.Clock.WhenWaitingForAsync(Flush);

        await world.RunClockUntilAsync(() => world.Flush.Flushes >= 3, Flush);
        var before = world.Flush.Flushes;
        await world.StopAsync();

        world.Flush.Flushes.ShouldBe(before + 1);
    }

    // ---- reconnect ----

    [Fact]
    public async Task After_a_drop_it_reconnects_after_the_back_off_and_subscribes_the_whole_set_again_from_the_database()
    {
        using var world = new FeedWorld(settings: Quiet);
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        world.Provider.Candidates.Add(Holding(Guid.NewGuid(), "11536", 100m)); // changed meanwhile, before the drop is seen

        world.Connector.Connection(0)!.Close();
        await world.RunClockUntilAsync(() => world.Connector.Connection(1) is { Subscribed.Count: > 0 }, TimeSpan.FromMilliseconds(500));

        var second = world.Connector.Connection(1)!;
        second.Subscribed.Single(s => s.Mode == FeedMode.Quote).Securities.Length.ShouldBe(2);
        world.Connector.Connection(0)!.Disposed.ShouldBeTrue();
        world.Connector.MaxOpen.ShouldBe(1);
        world.Metrics.Snapshot(world.Clock.GetUtcNow()).ReconnectsToday.ShouldBe(1);
        await world.StopAsync();
    }

    [Fact]
    public async Task The_back_off_grows_one_two_four_seconds_when_connections_give_no_data()
    {
        using var world = new FeedWorld(settings: Quiet);
        world.Begin();

        for (var i = 0; i < 3; i++)
        {
            await FeedWorld.UntilAsync(() => world.Connector.Connection(i) is not null);
            world.Connector.Connection(i)!.Close();
            await world.RunClockUntilAsync(() => world.Connector.Connection(i + 1) is not null, TimeSpan.FromMilliseconds(500));
        }

        world.Clock.Waits.Where(w => w != Quiet.FlushInterval && w != TimeSpan.FromSeconds(5)).Take(3).ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)]);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_connection_that_delivered_data_starts_the_back_off_again_from_one_second()
    {
        using var world = new FeedWorld(settings: Quiet);
        AddTypicalSet(world);
        world.Begin();
        await FeedWorld.UntilAsync(() => world.Connector.Connection(0) is not null);
        world.Connector.Connection(0)!.Close();
        await world.RunClockUntilAsync(() => world.Connector.Connection(1) is not null, TimeSpan.FromMilliseconds(500)); // waited 1 s
        await FeedWorld.UntilAsync(() => world.Manager.State == FeedStates.Connected);
        world.Connector.Connection(1)!.Close();
        await world.RunClockUntilAsync(() => world.Connector.Connection(2) is not null, TimeSpan.FromMilliseconds(500)); // waited 2 s
        await FeedWorld.UntilAsync(() => world.Manager.State == FeedStates.Connected);

        world.Connector.Connection(2)!.Push(Tick(FeedSegments.NseEquity, 2885, 100m, world.Clock.GetUtcNow()));
        await FeedWorld.UntilAsync(() => world.Flush.Added.Count == 1);
        world.Connector.Connection(2)!.Close();
        await world.RunClockUntilAsync(() => world.Connector.Connection(3) is not null, TimeSpan.FromMilliseconds(500));

        world.Clock.Waits.Where(w => w != Quiet.FlushInterval && w != TimeSpan.FromSeconds(5)).Take(3).ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)]);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_failed_connect_is_retried_after_the_back_off()
    {
        using var world = new FeedWorld(settings: Quiet);
        world.Connector.Enqueue(FeedConnectResult.Failed(FeedConnectFailureKind.Failed, "down"));
        AddTypicalSet(world);
        world.Begin();

        await world.RunClockUntilAsync(() => world.Connector.Connection(0) is { Subscribed.Count: > 0 }, TimeSpan.FromMilliseconds(500));

        world.Connector.Attempts.ShouldBe(2);
        world.Log.Events.ShouldContain(e => e.Event == 4110);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_connector_that_throws_is_a_failed_attempt_and_only_the_type_of_the_exception_is_logged()
    {
        using var world = new FeedWorld(settings: Quiet);
        world.Connector.ThrowNext = new InvalidOperationException("wss://api-feed.dhan.co?token=SECRET-TOKEN-TEXT");
        AddTypicalSet(world);
        world.Begin();

        await world.RunClockUntilAsync(() => world.Connector.Connection(0) is { Subscribed.Count: > 0 }, TimeSpan.FromMilliseconds(500));

        world.Connector.Attempts.ShouldBe(2);
        world.Log.Events.ShouldContain(e => e.Event == 4115 && e.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
        world.Log.Events.ShouldAllBe(e => !e.Message.Contains("SECRET-TOKEN-TEXT", StringComparison.Ordinal));
        await world.StopAsync();
    }

    [Fact]
    public async Task More_than_ten_reconnects_in_five_minutes_end_the_session_as_flapping()
    {
        using var world = new FeedWorld(settings: Quiet);
        world.Connector.AlwaysFail = FeedConnectFailureKind.Failed;

        var run = world.Start();
        await world.RunClockUntilAsync(() => run.IsCompleted, TimeSpan.FromSeconds(1));
        var result = await run;

        result.End.ShouldBe(FeedSessionEnd.Flapping);
        world.Connector.Attempts.ShouldBe(11); // the first connect and ten reconnects; the eleventh reconnect is refused
        world.Log.Events.ShouldContain(e => e.Event == 4106);
    }

    [Fact]
    public async Task Connections_that_hold_for_a_while_are_not_flapping()
    {
        using var world = new FeedWorld(settings: Quiet);
        world.Provider.Candidates.Add(Holding(Reliance, "2885", 5000m)); // no index: a minute without a packet is not silence here
        world.Begin();

        for (var i = 0; i < 12; i++)
        {
            await FeedWorld.UntilAsync(() => world.Connector.Connection(i) is { Subscribed.Count: > 0 });
            world.Clock.Advance(TimeSpan.FromMinutes(1)); // each connection lives a minute
            world.Connector.Connection(i)!.Close();
            await world.RunClockUntilAsync(() => world.Connector.Connection(i + 1) is not null, TimeSpan.FromSeconds(5));
        }

        world.Manager.State.ShouldNotBe(FeedStates.Flapping);
        world.Log.Events.ShouldNotContain(e => e.Event == 4106);
        await world.StopAsync();
    }

    // ---- silence ----

    [Fact]
    public async Task No_packet_for_thirty_seconds_in_market_hours_counts_as_dead_and_the_feed_reconnects()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        await world.RunClockUntilAsync(() => world.Connector.Connection(1) is not null, Flush);

        world.Connector.Connection(0)!.Disposed.ShouldBeTrue();
        world.Log.Events.ShouldContain(e => e.Event == 4113);
        await world.StopAsync();
    }

    [Fact]
    public async Task Silence_shorter_than_the_limit_does_not_close_the_connection()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        var start = world.Flush.Flushes;
        await world.RunClockUntilAsync(() => world.Flush.Flushes >= start + 10, Flush); // 20 s

        world.Connector.Attempts.ShouldBe(1);
        world.Connector.Connection(0)!.Disposed.ShouldBeFalse();
        await world.StopAsync();
    }

    [Fact]
    public async Task A_packet_now_and_then_keeps_the_connection_alive()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        var connection = world.Connector.Connection(0)!;

        for (var i = 0; i < 6; i++)
        {
            connection.Push(Tick(FeedSegments.Index, 13, 24800m + i, world.Clock.GetUtcNow()));
            var seen = world.Flush.Added.Count;
            await FeedWorld.UntilAsync(() => world.Flush.Added.Count > seen);
            var flushes = world.Flush.Flushes;
            await world.RunClockUntilAsync(() => world.Flush.Flushes >= flushes + 7, Flush); // 14 s of quiet each time, 84 s in all
        }

        world.Connector.Attempts.ShouldBe(1);
        await world.StopAsync();
    }

    [Fact]
    public async Task Silence_outside_market_hours_is_not_dead()
    {
        using var world = new FeedWorld(start: new DateTimeOffset(2026, 10, 12, 9, 2, 0, FeedWorld.Ist)); // pre-open
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        var start = world.Flush.Flushes;
        await world.RunClockUntilAsync(() => world.Flush.Flushes >= start + 15, Flush); // 30 s, still before 09:15

        world.Connector.Attempts.ShouldBe(1);
        await world.StopAsync();
    }

    [Fact]
    public async Task Without_an_index_in_the_set_silence_is_left_to_the_sockets_own_ping()
    {
        using var world = new FeedWorld();
        world.Provider.Candidates.Add(Holding(Reliance, "2885", 5000m));
        await world.StartConnectedAsync();

        var start = world.Flush.Flushes;
        await world.RunClockUntilAsync(() => world.Flush.Flushes >= start + 25, Flush); // 50 s

        world.Connector.Attempts.ShouldBe(1);
        await world.StopAsync();
    }

    // ---- the set changes ----

    [Fact]
    public async Task A_notification_applies_the_difference_without_a_reconnect()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        var added = Guid.NewGuid();
        world.Provider.Candidates.Add(Holding(added, "11536", 100m));
        world.Provider.Candidates.RemoveAll(c => c.InstrumentId == Infosys);

        world.Changes.Raise();
        await world.RunClockUntilAsync(() => world.Connector.Connection(0)!.Unsubscribed.Count > 0, Flush);

        var connection = world.Connector.Connection(0)!;
        var subscribed = connection.Subscribed[connection.Subscribed.Count - 1];
        subscribed.Mode.ShouldBe(FeedMode.Quote);
        subscribed.Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 11536)]);
        var unsubscribed = connection.Unsubscribed.ShouldHaveSingleItem();
        unsubscribed.Mode.ShouldBe(FeedMode.Ticker);
        unsubscribed.Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 1594)]);
        world.Connector.Attempts.ShouldBe(1);
        world.Metrics.Snapshot(world.Clock.GetUtcNow()).Subscribed.ShouldBe(3);
        await world.StopAsync();
    }

    [Fact]
    public async Task Without_a_notification_the_set_is_read_again_after_a_minute()
    {
        using var world = new FeedWorld();
        world.Provider.Candidates.Add(Holding(Reliance, "2885", 5000m)); // no index: this test lets a minute pass without a packet
        await world.StartConnectedAsync();
        var messages = world.Connector.Connection(0)!.Subscribed.Count;
        world.Provider.Candidates.Add(Holding(Guid.NewGuid(), "11536", 100m));

        await world.RunClockUntilAsync(() => world.Connector.Connection(0)!.Subscribed.Count > messages, TimeSpan.FromSeconds(10));

        world.Connector.Attempts.ShouldBe(1);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_tick_of_an_instrument_added_by_the_difference_is_taken_at_once()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        var added = Guid.NewGuid();
        world.Provider.Candidates.Add(Holding(added, "11536", 100m));
        world.Changes.Raise();
        await world.RunClockUntilAsync(() => world.Connector.Connection(0)!.Subscribed.Count > 2, Flush);

        world.Connector.Connection(0)!.Push(Tick(FeedSegments.NseEquity, 11536, 55m, world.Clock.GetUtcNow()));
        await FeedWorld.UntilAsync(() => world.Flush.Added.Count == 1);

        world.Flush.Added[0].InstrumentId.ShouldBe(added);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_mode_change_unsubscribes_the_old_mode_and_subscribes_the_new_one()
    {
        using var world = new FeedWorld();
        world.Provider.Candidates.AddRange([Alert(Reliance, "2885"), Index(Nifty, "13")]);
        await world.StartConnectedAsync();
        world.Provider.Candidates.Add(Holding(Reliance, "2885", 100m)); // the alert instrument is now held too

        world.Changes.Raise();
        await world.RunClockUntilAsync(() => world.Connector.Connection(0)!.Unsubscribed.Count > 0, Flush);

        var connection = world.Connector.Connection(0)!;
        var unsubscribed = connection.Unsubscribed.ShouldHaveSingleItem();
        unsubscribed.Mode.ShouldBe(FeedMode.Ticker);
        unsubscribed.Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 2885)]);
        var subscribed = connection.Subscribed[connection.Subscribed.Count - 1];
        subscribed.Mode.ShouldBe(FeedMode.Quote);
        subscribed.Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 2885)]);
        await world.StopAsync();
    }

    [Fact]
    public async Task A_set_that_cannot_be_read_leaves_the_subscriptions_as_they_are()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        world.Provider.Fail = new InvalidOperationException("database down");

        world.Changes.Raise();
        var start = world.Flush.Flushes;
        await world.RunClockUntilAsync(() => world.Flush.Flushes >= start + 3, Flush);

        world.Connector.Attempts.ShouldBe(1);
        world.Connector.Connection(0)!.Unsubscribed.ShouldBeEmpty();
        world.Log.Events.ShouldContain(e => e.Event == 4111);
        await world.StopAsync();
    }

    // ---- token ----

    [Fact]
    public async Task A_token_refused_at_connect_ends_the_session_and_does_not_try_again()
    {
        using var world = new FeedWorld();
        world.Connector.Enqueue(FeedConnectResult.Failed(FeedConnectFailureKind.TokenRefused, "refused"));
        AddTypicalSet(world);

        var result = await world.ResultAsync();

        result.End.ShouldBe(FeedSessionEnd.TokenRefused);
        result.CredentialFingerprint.ShouldNotBeNullOrEmpty();
        world.Connector.Attempts.ShouldBe(1);
        world.Log.Events.ShouldContain(e => e.Event == 4105);
    }

    [Fact]
    public async Task A_disconnect_packet_about_the_token_ends_the_session_as_refused()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        world.Connector.Connection(0)!.Push(new FeedPacket(FeedPacketKind.Disconnect, default, Reason: 807));
        var result = await world.ResultAsync();

        result.End.ShouldBe(FeedSessionEnd.TokenRefused);
        world.Connector.Attempts.ShouldBe(1);
        world.Connector.Connection(0)!.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_disconnect_packet_for_another_reason_is_an_ordinary_drop()
    {
        using var world = new FeedWorld(settings: Quiet);
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        world.Connector.Connection(0)!.Push(new FeedPacket(FeedPacketKind.Disconnect, default, Reason: 805));
        await world.RunClockUntilAsync(() => world.Connector.Connection(1) is not null, TimeSpan.FromMilliseconds(500));

        world.Connector.Attempts.ShouldBe(2);
        await world.StopAsync();
    }

    [Fact]
    public async Task Without_a_usable_token_nothing_is_connected()
    {
        using var world = new FeedWorld();
        world.Credentials.Token = null;

        var result = await world.ResultAsync();

        result.End.ShouldBe(FeedSessionEnd.NoToken);
        world.Connector.Attempts.ShouldBe(0);
    }

    // ---- stopping ----

    [Fact]
    public async Task Cancellation_stops_cleanly_closes_the_connection_and_leaves_the_state_closed()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        var result = await world.StopAsync();

        result.End.ShouldBe(FeedSessionEnd.Stopped);
        world.Connector.Connection(0)!.Disposed.ShouldBeTrue();
        world.Manager.State.ShouldBe(FeedStates.Closed);
        world.Metrics.Snapshot(world.Clock.GetUtcNow()).Connected.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_while_waiting_to_reconnect_stops_at_once()
    {
        using var world = new FeedWorld(settings: Quiet);
        world.Connector.AlwaysFail = FeedConnectFailureKind.Failed;
        world.Begin();
        await world.Clock.WhenWaitingForAsync(TimeSpan.FromSeconds(1));

        var result = await world.StopAsync();

        result.End.ShouldBe(FeedSessionEnd.Stopped);
    }

    [Fact]
    public async Task A_run_after_a_stop_starts_again_cleanly()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();
        await world.StopAsync();

        using var second = new FeedWorld();
        AddTypicalSet(second);
        await second.StartConnectedAsync();

        second.Manager.State.ShouldBe(FeedStates.Connected);
        await second.StopAsync();
    }
}
