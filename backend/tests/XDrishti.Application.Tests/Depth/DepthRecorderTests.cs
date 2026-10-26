using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Depth;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Platform.Services;
using static XDrishti.Application.Tests.Depth.DepthTestData;

namespace XDrishti.Application.Tests.Depth;

/// <summary>The recording session over fakes (P7-16 criteria 2, 4, 7, 8): subscription, window, flush, retry, drop, disconnect codes, reconnects, flapping, the disk guard.</summary>
public sealed class DepthRecorderTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyList<DepthSetEntry> Entries =
    [
        new(Reliance, RelianceId, "RELIANCE", "armed", 1),
        new(Tcs, TcsId, "TCS", "turnover", 2),
    ];

    private sealed class World
    {
        public World(int hour = 10, int minute = 0, DepthSettings? settings = null)
        {
            Clock = new SettableClock(new DateTimeOffset(2026, 10, 12, hour, minute, 0, Ist));
            Settings = settings ?? DepthSettings.Off with { Enabled = true };
            var services = new MiniProvider().Singleton<IDataAccountCredentials>(Credentials).ScopedInstance<IDepthStore>(Store);
            Recorder = new DepthRecorder(services, Connector, Settings, Clock, NullLogger<DepthRecorder>.Instance);
        }

        public SettableClock Clock { get; }

        public DepthSettings Settings { get; }

        public FakeCredentials Credentials { get; } = new();

        public FakeDepthConnector Connector { get; } = new();

        public FakeDepthStore Store { get; } = new();

        public DepthRecorder Recorder { get; }

        public CancellationTokenSource Stop { get; } = new();

        public Task<DepthSessionResult> Start() => Recorder.RunAsync(Entries, Stop.Token);

        public async Task<DepthSessionResult> StopAsync(Task<DepthSessionResult> run)
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

    private static async Task<FakeDepthConnection> ConnectedAsync(World world, int index = 0)
    {
        await UntilAsync(() => world.Connector.Connections.Count > index && world.Connector.Connections[index].Subscriptions.Count > 0, "the session did not subscribe");
        return world.Connector.Connections[index];
    }

    [Fact]
    public async Task It_subscribes_the_whole_set_in_one_message_and_records_the_snapshots_in_the_window()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);

        connection.Subscriptions.ShouldHaveSingleItem().ShouldBe([RelianceId, TcsId]);
        connection.Push(Bid());
        connection.Push(Ask());
        await UntilAsync(() => world.Recorder.Stats.Snapshots == 1, "no snapshot");
        await world.Recorder.FlushAsync(Ct);
        var result = await world.StopAsync(run);

        result.End.ShouldBe(DepthSessionEnd.Stopped);
        world.Store.Rows.ShouldHaveSingleItem().InstrumentId.ShouldBe(Reliance);
        result.Stats.Snapshots.ShouldBe(1);
        result.Stats.Packets.ShouldBe(2);
        connection.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_snapshot_before_09_15_is_not_recorded()
    {
        var world = new World(9, 14);
        world.Clock.Advance(TimeSpan.FromSeconds(59)); // 09:14:59
        var run = world.Start();
        var connection = await ConnectedAsync(world);

        connection.Push(Bid());
        connection.Push(Ask());
        await UntilAsync(() => world.Recorder.Stats.Packets == 2, "packets not read");
        await world.Recorder.FlushAsync(Ct);
        await world.StopAsync(run);

        world.Store.Rows.ShouldBeEmpty();
        world.Recorder.Stats.Snapshots.ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_batch_is_kept_and_written_with_the_next_flush_without_a_duplicate()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);
        connection.Push(Bid());
        connection.Push(Ask());
        await UntilAsync(() => world.Recorder.Stats.Snapshots == 1, "no snapshot");

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
        connection.Push(Bid());
        connection.Push(Ask());
        await UntilAsync(() => world.Recorder.Stats.Snapshots == 1, "no snapshot");
        world.Store.FailAppends = 10;
        await world.Recorder.FlushAsync(Ct); // fails, kept

        world.Clock.Advance(TimeSpan.FromSeconds(31));
        connection.Push(Bid(quantity: 999));
        connection.Push(Ask(quantity: 999));
        await UntilAsync(() => world.Recorder.Stats.Snapshots == 2, "no second snapshot");
        world.Store.FailAppends = 0;
        await world.Recorder.FlushAsync(Ct);
        var result = await world.StopAsync(run);

        result.Stats.DroppedRows.ShouldBe(1);
        world.Store.Rows.ShouldHaveSingleItem().BidQty[0].ShouldBe(999);
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

        connection.Push(DepthPacket.Disconnect(code));
        var result = await run;

        result.End.ToString().ShouldBe(expected);
        world.Connector.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_ordinary_drop_is_followed_by_a_reconnect_with_the_same_set_and_the_gap_is_counted()
    {
        var world = new World();
        var run = world.Start();
        var first = await ConnectedAsync(world);

        first.Close();
        await UntilAsync(() => world.Clock.NextTimerDue is not null && world.Connector.Connections.Count == 1, "no back-off wait");
        await RunClockAsync(world, () => world.Connector.Connections.Count == 2);
        var second = await ConnectedAsync(world, 1);
        var result = await world.StopAsync(run);

        second.Subscriptions.ShouldHaveSingleItem().ShouldBe([RelianceId, TcsId]);
        first.Disposed.ShouldBeTrue();
        result.Stats.Reconnects.ShouldBe(1);
        result.Stats.GapSeconds.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task More_than_ten_reconnects_in_five_minutes_end_the_session_as_flapping()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);

        var index = 0;
        while (!run.IsCompleted && index < 40)
        {
            var current = world.Connector.Connections[^1];
            current.Close();
            var count = world.Connector.Connections.Count;
            await RunClockAsync(world, () => run.IsCompleted || world.Connector.Connections.Count > count);
            index++;
        }

        var result = await run;
        result.End.ShouldBe(DepthSessionEnd.Flapping);
        connection.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_refused_token_ends_the_session_at_once_and_there_is_no_second_attempt()
    {
        var world = new World();
        world.Connector.Scripted.Enqueue(DepthConnectResult.Failed(FeedConnectFailureKind.TokenRefused, "refused"));

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
    public async Task The_disk_guard_stops_the_run_when_the_stored_size_is_over_the_cap_and_keeps_what_was_queued()
    {
        var world = new World();
        var run = world.Start();
        var connection = await ConnectedAsync(world);
        connection.Push(Bid());
        connection.Push(Ask());
        await UntilAsync(() => world.Recorder.Stats.Snapshots == 1, "no snapshot");

        world.Store.Stored = (world.Settings.MaxStoredBytes) + 1;
        await RunClockAsync(world, () => run.IsCompleted, TimeSpan.FromSeconds(30));
        var result = await run;

        result.End.ShouldBe(DepthSessionEnd.DiskBudget);
        world.Store.Rows.ShouldHaveSingleItem(); // the final flush wrote the queue; nothing was deleted
    }

    [Fact]
    public async Task The_disk_guard_stops_the_run_when_the_free_space_is_under_the_floor()
    {
        var world = new World();
        var run = world.Start();
        await ConnectedAsync(world);

        world.Store.Free = world.Settings.MinFreeBytes - 1;
        await RunClockAsync(world, () => run.IsCompleted, TimeSpan.FromSeconds(30));

        (await run).End.ShouldBe(DepthSessionEnd.DiskFree);
    }

    [Fact]
    public async Task Unreadable_free_space_does_not_stop_the_run()
    {
        var world = new World();
        var run = world.Start();
        await ConnectedAsync(world);

        world.Store.Free = null;
        await RunClockAsync(world, () => false, TimeSpan.FromSeconds(30), steps: 80);
        run.IsCompleted.ShouldBeFalse();
        await world.StopAsync(run);
    }

    /// <summary>Moves the clock in small steps, letting the code under test act after each, until the condition holds.</summary>
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
