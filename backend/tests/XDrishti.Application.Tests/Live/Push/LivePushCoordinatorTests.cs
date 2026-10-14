using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Tests.Live.Push;

/// <summary>The hub's push (ADR 0024 §7): scope, only what the connection subscribed to, only what changed, bounded messages, the status and the end of a session.</summary>
public sealed class LivePushCoordinatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid SessionOne = Guid.NewGuid();
    private static readonly Guid SessionTwo = Guid.NewGuid();

    // ---- connecting and the scope ----

    [Fact]
    public async Task A_new_connection_is_sent_the_feed_status_at_once_and_no_prices()
    {
        var world = new PushWorld();
        world.StatusHandler.Status = new LiveFeedStatus("connected", 12, PushWorld.Now);

        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);
        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.Statuses.ShouldBe([("c1", new LiveFeedStatus("connected", 12, PushWorld.Now))]);
        world.Clients.Prices.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_status_that_cannot_be_sent_does_not_fail_the_connection()
    {
        var world = new PushWorld();
        world.Clients.Gone.Add("c1");

        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);

        world.Coordinator.ConnectionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Subscribe_hands_the_scope_to_the_resolver_and_answers_how_many_instruments_it_follows()
    {
        var world = new PushWorld();
        var (account, bucket, holding) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        world.Resolver.Result = [A, B];
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);

        var ack = await world.Coordinator.SubscribeAsync("c1", new LiveScope(account, bucket, [holding]), Ct);

        ack.ShouldBe(new SubscribeAck(2, false, false));
        var request = world.Resolver.Requests.ShouldHaveSingleItem();
        (request.AccountId, request.BucketId).ShouldBe((account, bucket));
        request.HoldingIds.ShouldBe([holding]);
    }

    [Fact]
    public async Task A_missing_scope_is_the_default_scope()
    {
        var world = new PushWorld();
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);

        await world.Coordinator.SubscribeAsync("c1", null, Ct);

        var request = world.Resolver.Requests.ShouldHaveSingleItem();
        (request.AccountId, request.BucketId).ShouldBe((null, null));
        request.HoldingIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task More_holding_ids_than_the_limit_are_not_read_and_the_answer_says_truncated()
    {
        var world = new PushWorld(PushSettings.Default with { MaxHoldingIds = 3 });
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        var ack = await world.Coordinator.SubscribeAsync("c1", new LiveScope(null, null, ids), Ct);

        ack.Truncated.ShouldBeTrue();
        world.Resolver.Requests.Single().HoldingIds.ShouldBe([.. ids.Take(3)]);
    }

    [Fact]
    public async Task More_instruments_than_the_limit_are_cut_to_the_largest_and_the_answer_says_truncated()
    {
        var world = new PushWorld(PushSettings.Default with { MaxInstrumentsPerConnection = 2 });
        world.Reader.Set(A, 10, PushWorld.Now);
        world.Reader.Set(B, 20, PushWorld.Now);
        world.Reader.Set(C, 30, PushWorld.Now);

        var ack = await world.FollowAsync("c1", SessionOne, A, B, C); // the resolver lists the largest first
        await world.Coordinator.PushPricesAsync(Ct);

        ack.ShouldBe(new SubscribeAck(2, true, false));
        world.Clients.PricesOf("c1").SelectMany(m => m.Instruments).Select(p => p.InstrumentId).ShouldBe([A, B]);
    }

    [Fact]
    public async Task A_scope_the_database_cannot_read_is_answered_as_failed_and_the_connection_follows_nothing()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        world.Resolver.Failure = new InvalidOperationException("db down");

        var ack = await world.Coordinator.SubscribeAsync("c1", new LiveScope(null, null, null), Ct);
        await world.Coordinator.PushPricesAsync(Ct);

        ack.ShouldBe(new SubscribeAck(0, false, true));
        world.Clients.Prices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Subscribing_on_an_unknown_connection_follows_nothing()
    {
        var world = new PushWorld();

        var ack = await world.Coordinator.SubscribeAsync("nobody", new LiveScope(null, null, null), Ct);

        ack.ShouldBe(new SubscribeAck(0, false, false));
        world.Resolver.Requests.ShouldBeEmpty();
    }

    // ---- what is pushed ----

    [Fact]
    public async Task Each_connection_gets_only_the_prices_of_what_it_subscribed_to()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        world.Reader.Set(B, 20, PushWorld.Now);
        world.Reader.Set(C, 30, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A, B);
        await world.FollowAsync("c2", SessionTwo, B, C);

        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.PricesOf("c1").SelectMany(m => m.Instruments).Select(p => p.InstrumentId).ShouldBe([A, B]);
        world.Clients.PricesOf("c2").SelectMany(m => m.Instruments).Select(p => p.InstrumentId).ShouldBe([B, C]);
    }

    [Fact]
    public async Task The_prices_of_all_connections_are_read_once_for_the_union_of_their_instruments()
    {
        var world = new PushWorld();
        await world.FollowAsync("c1", SessionOne, A, B);
        await world.FollowAsync("c2", SessionTwo, B, C);

        await world.Coordinator.PushPricesAsync(Ct);

        world.Reader.Requests.ShouldHaveSingleItem().ShouldBe([A, B, C], ignoreOrder: true);
    }

    [Fact]
    public async Task A_price_is_sent_again_only_when_it_changed()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        world.Reader.Set(B, 20, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A, B);
        await world.Coordinator.PushPricesAsync(Ct);

        await world.Coordinator.PushPricesAsync(Ct); // nothing changed
        world.Reader.Set(B, 21, PushWorld.Now.AddSeconds(2));
        await world.Coordinator.PushPricesAsync(Ct);

        var messages = world.Clients.PricesOf("c1").ToList();
        messages.Count.ShouldBe(2);
        messages[1].Instruments.ShouldBe([new InstrumentPrice(B, 21, PushWorld.Now.AddSeconds(2), "live")]);
    }

    [Fact]
    public async Task A_change_of_an_instrument_nobody_follows_is_sent_to_nobody()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        world.Reader.Set(C, 30, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        await world.Coordinator.PushPricesAsync(Ct);

        world.Reader.Set(C, 31, PushWorld.Now.AddSeconds(1));
        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.PricesOf("c1").Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_second_subscribe_replaces_the_first_and_sends_the_new_scope_in_full()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        world.Reader.Set(B, 20, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        await world.Coordinator.PushPricesAsync(Ct);

        world.Resolver.Result = [B];
        await world.Coordinator.SubscribeAsync("c1", new LiveScope(null, null, null), Ct);
        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.PricesOf("c1").Last().Instruments.Select(p => p.InstrumentId).ShouldBe([B]);
    }

    [Fact]
    public async Task After_unsubscribe_and_after_disconnect_no_prices_are_sent()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("quiet", SessionOne, A);
        await world.FollowAsync("gone", SessionTwo, A);
        world.Coordinator.Unsubscribe("quiet");
        world.Coordinator.Disconnected("gone");

        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.Prices.ShouldBeEmpty();
        world.Reader.Calls.ShouldBe(0); // nobody to read for
    }

    [Fact]
    public async Task A_large_change_is_split_into_bounded_messages()
    {
        var world = new PushWorld();
        var ids = Enumerable.Range(0, 1200).Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var id in ids)
        {
            world.Reader.Set(id, 1234.5678m, PushWorld.Now);
        }

        await world.FollowAsync("c1", SessionOne, ids);
        await world.Coordinator.PushPricesAsync(Ct);

        var messages = world.Clients.PricesOf("c1").ToList();
        messages.ShouldAllBe(m => m.Instruments.Count <= LiveHubContract.MaxInstrumentsPerMessage);
        messages.ShouldAllBe(m => m.AsOf == PushWorld.Now);
        messages.SelectMany(m => m.Instruments).Count().ShouldBe(1000); // the connection follows at most 1000 instruments
    }

    [Fact]
    public async Task A_connection_that_went_away_does_not_stop_the_push_to_the_others()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        await world.FollowAsync("c2", SessionTwo, A);
        world.Clients.Gone.Add("c1");

        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.PricesOf("c2").Count().ShouldBe(1);
    }

    [Fact]
    public async Task Prices_that_could_not_be_sent_are_sent_at_the_next_push()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        world.Clients.Gone.Add("c1");
        await world.Coordinator.PushPricesAsync(Ct);
        world.Clients.Gone.Remove("c1");

        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.PricesOf("c1").ShouldHaveSingleItem().Instruments.Select(p => p.InstrumentId).ShouldBe([A]);
    }

    [Fact]
    public async Task A_resubscribe_with_a_longer_list_sends_only_the_instruments_that_are_new()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        world.Reader.Set(B, 20, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        await world.Coordinator.PushPricesAsync(Ct);

        world.Resolver.Result = [A, B];
        await world.Coordinator.SubscribeAsync("c1", new LiveScope(null, null, null), Ct);
        await world.Coordinator.PushPricesAsync(Ct);

        world.Clients.PricesOf("c1").Last().Instruments.Select(p => p.InstrumentId).ShouldBe([B]);
    }

    // ---- the status ----

    [Fact]
    public async Task The_status_is_pushed_when_it_changes_and_at_least_every_15_seconds_otherwise_not()
    {
        var world = new PushWorld();
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);
        world.Clients.Statuses.Clear();

        await world.Coordinator.PushStatusAsync(Ct); // the first look sends
        world.Clients.Statuses.Count.ShouldBe(1);

        world.Clock.Advance(TimeSpan.FromSeconds(5));
        await world.Coordinator.PushStatusAsync(Ct); // unchanged, not due
        world.Clients.Statuses.Count.ShouldBe(1);

        world.StatusHandler.Status = new LiveFeedStatus("reconnecting", 12, null);
        await world.Coordinator.PushStatusAsync(Ct); // changed
        world.Clients.Statuses.Count.ShouldBe(2);

        world.Clock.Advance(TimeSpan.FromSeconds(14.9));
        await world.Coordinator.PushStatusAsync(Ct);
        world.Clients.Statuses.Count.ShouldBe(2);

        world.Clock.Advance(TimeSpan.FromSeconds(0.1));
        await world.Coordinator.PushStatusAsync(Ct); // 15 s since the last push
        world.Clients.Statuses.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_change_of_the_number_of_followed_instruments_is_a_change_of_status()
    {
        var world = new PushWorld();
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);
        await world.Coordinator.PushStatusAsync(Ct);
        world.Clients.Statuses.Clear();

        world.StatusHandler.Status = new LiveFeedStatus("connected", 13, null);
        await world.Coordinator.PushStatusAsync(Ct);

        world.Clients.Statuses.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_nobody_connected_nothing_is_sent()
    {
        var world = new PushWorld();

        await world.Coordinator.PushStatusAsync(Ct);

        world.Clients.Statuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_status_goes_to_every_connection_and_one_that_is_gone_does_not_stop_the_others()
    {
        var world = new PushWorld();
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);
        await world.Coordinator.ConnectedAsync("c2", SessionTwo, Ct);
        world.Clients.Statuses.Clear();
        world.Clients.Gone.Add("c1");

        await world.Coordinator.PushStatusAsync(Ct);

        world.Clients.Statuses.Select(s => s.Connection).ShouldBe(["c2"]);
    }

    // ---- sessions ----

    [Fact]
    public async Task A_connection_whose_session_ended_is_disconnected_and_the_others_are_not()
    {
        var world = new PushWorld();
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);
        await world.Coordinator.ConnectedAsync("c1b", SessionOne, Ct);
        await world.Coordinator.ConnectedAsync("c2", SessionTwo, Ct);
        world.EndedSessions.Ended.Add(SessionOne);

        await world.Coordinator.EndSessionsAsync(Ct);

        world.Clients.Disconnected.ShouldBe(["c1", "c1b"], ignoreOrder: true);
        world.EndedSessions.Asked.ShouldHaveSingleItem().ShouldBe([SessionOne, SessionTwo], ignoreOrder: true);
        world.Coordinator.ConnectionCount.ShouldBe(1);
    }

    [Fact]
    public async Task While_every_session_is_active_nobody_is_disconnected()
    {
        var world = new PushWorld();
        await world.Coordinator.ConnectedAsync("c1", SessionOne, Ct);

        await world.Coordinator.EndSessionsAsync(Ct);

        world.Clients.Disconnected.ShouldBeEmpty();
    }

    // ---- the loop: bursts are folded, a sweep catches what no notification announced ----

    private static async Task<(Task Loop, CancellationTokenSource Stop)> StartLoopAsync(PushWorld world)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var loop = Task.Run(() => world.Coordinator.PriceLoopAsync(stop.Token), Ct);
        await world.Clock.WaitForTimerAsync(PushSettings.Default.SweepInterval, Ct); // it is waiting for a notification (or the sweep)
        return (loop, stop);
    }

    [Fact]
    public async Task A_burst_of_notifications_is_folded_into_one_push_and_the_next_waits_a_second()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A); // the subscribe asks for a push
        var (loop, stop) = await StartLoopAsync(world);

        await world.Reader.WaitForCallsAsync(1, Ct); // the first push
        await world.Clock.WaitForTimerAsync(PushSettings.Default.MinPushInterval, Ct); // ... and the loop holds back for a second
        world.Reader.Set(A, 11, PushWorld.Now.AddSeconds(1));
        for (var i = 0; i < 10; i++)
        {
            world.Coordinator.Wake(); // ten notifications
        }

        world.Clock.Advance(PushSettings.Default.MinPushInterval - TimeSpan.FromMilliseconds(1));
        world.Reader.Calls.ShouldBe(1); // nothing goes out before the second is over

        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await world.Reader.WaitForCallsAsync(2, Ct);
        await world.Clock.WaitForTimerAsync(PushSettings.Default.MinPushInterval, Ct);
        await stop.CancelAsync();
        await loop;

        world.Reader.Calls.ShouldBe(2); // ten notifications, one push
        world.Clients.PricesOf("c1").Select(m => m.Instruments.Single().LastPrice).ShouldBe([10m, 11m]);
    }

    [Fact]
    public async Task Without_any_notification_the_prices_are_read_every_thirty_seconds()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        var (loop, stop) = await StartLoopAsync(world);
        await world.Reader.WaitForCallsAsync(1, Ct);
        await world.Clock.WaitForTimerAsync(PushSettings.Default.MinPushInterval, Ct);
        world.Clock.Advance(PushSettings.Default.MinPushInterval);
        await world.Clock.WaitForTimerAsync(PushSettings.Default.SweepInterval, Ct); // waiting again

        world.Reader.Set(A, 12, PushWorld.Now.AddSeconds(5)); // a quote written without a notification
        world.Clock.Advance(PushSettings.Default.SweepInterval);
        await world.Reader.WaitForCallsAsync(2, Ct);
        await world.Clock.WaitForTimerAsync(PushSettings.Default.MinPushInterval, Ct);
        await stop.CancelAsync();
        await loop;

        world.Clients.PricesOf("c1").Last().Instruments.Single().LastPrice.ShouldBe(12m);
    }

    [Fact]
    public async Task A_failing_read_is_survived_and_the_loop_goes_on()
    {
        var world = new PushWorld();
        world.Reader.Set(A, 10, PushWorld.Now);
        await world.FollowAsync("c1", SessionOne, A);
        world.Reader.Failure = new InvalidOperationException("db restarting");
        var (loop, stop) = await StartLoopAsync(world);
        await world.Clock.WaitForTimerAsync(PushSettings.Default.MinPushInterval, Ct); // the failure was logged and the loop paused

        world.Reader.Failure = null;
        world.Clock.Advance(PushSettings.Default.MinPushInterval);
        world.Coordinator.Wake();
        await world.Reader.WaitForCallsAsync(1, Ct);
        await stop.CancelAsync();
        await loop;

        world.Clients.PricesOf("c1").Count().ShouldBe(1);
    }
}
