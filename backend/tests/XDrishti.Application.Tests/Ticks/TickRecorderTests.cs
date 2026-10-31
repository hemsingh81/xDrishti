using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Ticks;
using static XDrishti.Application.Tests.Ticks.TickTestData;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>The tick session over fakes (P7-16b criteria 1, 3, 7 to 9): quote-mode subscription in batches of 100, change-only rows, flush, retry, drop, disconnect codes, reconnect with a baseline and a counted gap, the disk guard.</summary>
public sealed class TickRecorderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyList<TickSetEntry> Entries =
    [
        new(Reliance, RelianceId, "RELIANCE", TickSources.DepthSet, "armed", 1),
        new(Tcs, TcsId, "TCS", TickSources.DepthSet, "turnover", 2),
    ];

    private sealed class World
    {
        public World(int hour = 10, int minute = 0, TickSettings? settings = null, int maxPending = TickRecorder.MaxPending)
        {
            Clock = new SettableClock(new DateTimeOffset(2026, 10, 12, hour, minute, 0, Ist));
            Settings = settings ?? TickSettings.Off with { Enabled = true };
            var services = new MiniProvider().Singleton<IDataAccountCredentials>(Credentials).ScopedInstance<ITickStore>(Store);
            Recorder = new TickRecorder(services, Connector, Settings, Clock, NullLogger<TickRecorder>.Instance, maxPending);
        }

        public SettableClock Clock { get; }

        public TickSettings Settings { get; }

        public FakeCredentials Credentials { get; } = new();

        public FakeTickConnector Connector { get; } = new();

        public FakeTickStore Store { get; } = new();

        public TickRecorder Recorder { get; }

        public CancellationTokenSource Stop { get; } = new();

        public Task<TickSessionResult> Start(IReadOnlyList<TickSetEntry>? set = null) => Recorder.RunAsync(set ?? Entries, Stop.Token);

        public FeedPacket Quote(long volume, int securityId = RelianceId) => TickTestData.Quote(volume, ltt: Clock.GetUtcNow(), securityId: securityId);

        public async Task<TickSessionResult> StopAsync(Task<TickSessionResult> run)
        {
            await Stop.CancelAsync();
            return await run;
        }
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 1_000_000 && !condition(); i++)
        {
            await Task.Yield();
        }

        condition().ShouldBeTrue(what);
    }

    private static async Task<FakeTickConnection> ConnectedAsync(World world, int index = 0)
    {
        await UntilAsync(() => world.Connector.Connections.Count > index && world.Connector.Connections[index].Subscriptions.Count > 0 && world.Recorder.State == DepthStates.Recording, "the session did not subscribe");
        return world.Connector.Connections[index];
    }

    [Fact]
    public async Task It_subscribes_the_set_in_quote_mode_only_and_writes_rows_only_when_the_volume_rose()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);

        var subscription = connection.Subscriptions.ShouldHaveSingleItem();
        subscription.Mode.ShouldBe(FeedMode.Quote);
        subscription.Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, RelianceId), new FeedSecurity(FeedSegments.NseEquity, TcsId)]);
        connection.Push(world.Quote(1_000));
        connection.Push(world.Quote(1_000));
        connection.Push(world.Quote(1_050));
        await UntilAsync(() => world.Recorder.Stats.Packets == 3, "packets not read");
        await world.Recorder.FlushAsync(Ct);
        var result = await world.StopAsync(run);

        world.Store.Rows.Select(r => (r.CumVol, r.Flags, r.RxSeq)).ShouldBe([(1_000L, TickFlags.Baseline, 1L), (1_050L, (short)0, 3L)]);
        result.Stats.Rows.ShouldBe(2);
        result.Stats.Unchanged.ShouldBe(1);
        connection.Unsubscribes.ShouldBe(0);
        connection.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_set_of_more_than_one_hundred_is_subscribed_in_messages_of_at_most_one_hundred()
    {
        var world = new World();
        var big = Enumerable.Range(1, 230).Select(i => new TickSetEntry(Guid.NewGuid(), i, "S" + i, TickSources.Built, "turnover", i)).ToList();
        var run = world.Start(big);
        var connection = await ConnectedAsync(world);
        await world.StopAsync(run);

        connection.Subscriptions.Select(s => s.Securities.Count).ShouldBe([100, 100, 30]);
        connection.Subscriptions.ShouldAllBe(s => s.Mode == FeedMode.Quote);
    }

    [Fact]
    public async Task A_quote_before_09_15_is_not_recorded()
    {
        var world = new World(9, 14);
        var run = world.Start();
        var connection = await ConnectedAsync(world);

        connection.Push(world.Quote(1_000));
        await UntilAsync(() => world.Recorder.Stats.Packets == 1, "packet not read");
        await world.Recorder.FlushAsync(Ct);
        await world.StopAsync(run);

        world.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_batch_is_kept_and_written_with_the_next_flush_without_a_duplicate()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);
        connection.Push(world.Quote(1_000));
        await UntilAsync(() => world.Recorder.Stats.Rows == 1, "no row");

        world.Store.FailAppends = 1;
        await world.Recorder.FlushAsync(Ct);
        world.Store.Rows.ShouldBeEmpty();
        await world.Recorder.FlushAsync(Ct);
        await world.Recorder.FlushAsync(Ct);
        await world.StopAsync(run);

        world.Store.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Rows_unwritten_for_more_than_thirty_seconds_are_dropped_oldest_first_and_counted()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);
        connection.Push(world.Quote(1_000));
        await UntilAsync(() => world.Recorder.Stats.Rows == 1, "no row");
        world.Store.FailAppends = 10;
        await world.Recorder.FlushAsync(Ct);

        world.Clock.Advance(TimeSpan.FromSeconds(31));
        connection.Push(world.Quote(2_000));
        await UntilAsync(() => world.Recorder.Stats.Rows == 2, "no second row");
        world.Store.FailAppends = 0;
        await world.Recorder.FlushAsync(Ct);
        var result = await world.StopAsync(run);

        result.Stats.DroppedRows.ShouldBe(1);
        world.Store.Rows.ShouldHaveSingleItem().CumVol.ShouldBe(2_000);
    }

    [Fact]
    public async Task A_database_that_hangs_cannot_grow_the_queue_without_limit_the_surplus_is_dropped_and_counted()
    {
        const int Cap = 100;
        var world = new World(maxPending: Cap);
        var run = world.Start();
        var connection = await ConnectedAsync(world);
        var total = Cap + 50;
        for (var i = 1; i <= total; i++)
        {
            connection.Push(world.Quote(i));
        }

        await UntilAsync(() => world.Recorder.Stats.Packets == total, "packets not read");
        await world.Recorder.FlushAsync(Ct);
        var result = await world.StopAsync(run);

        world.Store.Rows.Count.ShouldBe(Cap);
        TickRecorder.MaxPending.ShouldBe(50_000);
        result.Stats.DroppedRows.ShouldBe(50);
    }

    [Theory]
    [InlineData(806, "NotSubscribed")]
    [InlineData(805, "Flapping")]
    [InlineData(807, "TokenRefused")]
    [InlineData(808, "TokenRefused")]
    [InlineData(809, "TokenRefused")]
    public async Task A_disconnect_code_ends_the_session_the_way_the_code_says_and_nothing_reconnects(int code, string expected)
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);

        connection.Push(new FeedPacket(FeedPacketKind.Disconnect, default, Reason: code));
        var result = await run;

        result.End.ToString().ShouldBe(expected);
        world.Connector.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task After_a_drop_it_reconnects_with_the_same_set_counts_the_gap_and_the_first_row_is_a_baseline()
    {
        var world = new World();
        var run = world.Start();
        var first = await ConnectedAsync(world);
        first.Push(world.Quote(1_000));
        await UntilAsync(() => world.Recorder.Stats.Rows == 1, "no row");

        first.Close();
        await RunClockAsync(world, () => world.Connector.Connections.Count == 2);
        var second = await ConnectedAsync(world, 1);
        second.Push(world.Quote(5_000));
        await UntilAsync(() => world.Recorder.Stats.Rows == 2, "no row after the reconnect");
        await world.Recorder.FlushAsync(Ct);
        var result = await world.StopAsync(run);

        second.Subscriptions.ShouldHaveSingleItem().Securities.Count.ShouldBe(2);
        world.Store.Rows[^1].Flags.ShouldBe(TickFlags.Baseline);
        world.Store.Rows[^1].RxSeq.ShouldBe(2, "the packet counter runs on through the run");
        result.Stats.Reconnects.ShouldBe(1);
        result.Stats.GapSeconds.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task More_than_ten_reconnects_in_five_minutes_end_the_session_as_flapping()
    {
        var world = new World();
        var run = world.Start();
        await ConnectedAsync(world);

        for (var index = 0; !run.IsCompleted && index < 40; index++)
        {
            world.Connector.Connections[^1].Close();
            var count = world.Connector.Connections.Count;
            await RunClockAsync(world, () => run.IsCompleted || world.Connector.Connections.Count > count);
        }

        (await run).End.ShouldBe(DepthSessionEnd.Flapping);
    }

    [Fact]
    public async Task A_refused_token_ends_the_session_at_once_and_there_is_no_second_attempt()
    {
        var world = new World();
        world.Connector.Scripted.Enqueue(FeedConnectResult.Failed(FeedConnectFailureKind.TokenRefused, "refused"));

        var result = await world.Start();

        result.End.ShouldBe(DepthSessionEnd.TokenRefused);
        result.CredentialFingerprint.ShouldNotBeNull();
        world.Connector.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_usable_token_nothing_connects()
    {
        var world = new World();
        world.Credentials.Token = null;

        var result = await world.Start();

        result.End.ShouldBe(DepthSessionEnd.NoToken);
        world.Connector.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_disk_guard_stops_the_run_over_the_own_cap_and_keeps_what_was_queued()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);
        connection.Push(world.Quote(1_000));
        await UntilAsync(() => world.Recorder.Stats.Rows == 1, "no row");

        world.Store.Stored = world.Settings.MaxStoredBytes + 1;
        await RunClockAsync(world, () => run.IsCompleted, TimeSpan.FromSeconds(30));

        (await run).End.ShouldBe(DepthSessionEnd.DiskBudget);
        world.Store.Rows.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_disk_guard_stops_the_run_under_the_shared_free_floor()
    {
        var world = new World();
        var run = world.Start();
        await ConnectedAsync(world);

        world.Store.Free = world.Settings.MinFreeBytes - 1;
        await RunClockAsync(world, () => run.IsCompleted, TimeSpan.FromSeconds(30));

        (await run).End.ShouldBe(DepthSessionEnd.DiskFree);
    }

    private static async Task RunClockAsync(World world, Func<bool> done, TimeSpan? step = null, int steps = 400)
    {
        for (var i = 0; i < steps && !done(); i++)
        {
            world.Clock.Advance(step ?? TimeSpan.FromSeconds(2));
            for (var spin = 0; spin < 200; spin++)
            {
                await Task.Yield();
            }
        }
    }
}
