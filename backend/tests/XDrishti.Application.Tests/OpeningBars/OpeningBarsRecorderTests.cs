using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.OpeningBars;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Ticks;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>
/// The opening-minute session over fakes (BK-03b criteria 2, 8, 9, 11): quote-mode subscription in batches of 100, bars from exchange time written in one statement and insert-only, gap events for a drop and a silence, the stop at 09:31 and on low disk,
/// refusals, a failed write kept for the next second.
/// </summary>
public sealed class OpeningBarsRecorderTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly DateOnly Day = new(2026, 10, 12);
    private static readonly Guid IdA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid IdB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(int hour, int minute, int second = 0) => new(2026, 10, 12, hour, minute, second, Ist);

    private static readonly IReadOnlyList<OpeningSetEntry> Set = [new(IdA, 1001, "A"), new(IdB, 1002, "B")];

    private sealed class World
    {
        public World(int hour = 9, int minute = 14, OpeningBarsSettings? settings = null)
        {
            Clock = new SettableClock(At(hour, minute));
            Settings = settings ?? OpeningBarsSettings.Off with { Enabled = true, Silence = TimeSpan.FromHours(1) };
            var services = new MiniProvider().Singleton<IDataAccountCredentials>(Credentials).ScopedInstance<IOpeningBarsStore>(Store);
            Recorder = new OpeningBarsRecorder(services, Connector, Settings, Clock, NullLogger<OpeningBarsRecorder>.Instance);
        }

        public SettableClock Clock { get; }

        public OpeningBarsSettings Settings { get; }

        public FakeCredentials Credentials { get; } = new();

        public FakeOpeningConnector Connector { get; } = new();

        public FakeOpeningStore Store { get; } = new();

        public OpeningBarsRecorder Recorder { get; }

        public CancellationTokenSource Stop { get; } = new();

        public Task<OpeningSessionResult> Start(IReadOnlyList<OpeningSetEntry>? set = null) => Recorder.RunAsync(Day, set ?? Set, Stop.Token);

        public async Task<OpeningSessionResult> StopAsync(Task<OpeningSessionResult> run)
        {
            await Stop.CancelAsync();
            return await run;
        }

        public async Task<FakeTickConnection> ConnectedAsync(int index = 0)
        {
            await UntilAsync(() => Connector.Connections.Count > index && Connector.Connections[index].Subscriptions.Count > 0, "the session did not subscribe");
            return Connector.Connections[index];
        }

        /// <summary>Moves the clock by whole seconds, letting the session's loops run between steps.</summary>
        public async Task StepAsync(TimeSpan total)
        {
            for (var left = total; left > TimeSpan.Zero; left -= TimeSpan.FromSeconds(1))
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                for (var i = 0; i < 300; i++)
                {
                    await Task.Yield();
                }
            }
        }

        public static FeedPacket Quote(int securityId, DateTimeOffset exchangeTime, decimal price, long volume) =>
            new(FeedPacketKind.Quote, new FeedSecurity(FeedSegments.NseEquity, securityId), price, exchangeTime, Volume: volume);
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 1_000_000 && !condition(); i++)
        {
            await Task.Yield();
        }

        condition().ShouldBeTrue(what);
    }

    [Fact]
    public async Task It_subscribes_in_quote_mode_only_in_messages_of_at_most_one_hundred_and_records_the_start()
    {
        var world = new World();
        var big = Enumerable.Range(1, 230).Select(i => new OpeningSetEntry(Guid.NewGuid(), i, "S" + i)).ToList();
        var run = world.Start(big);

        var connection = await world.ConnectedAsync();
        await world.StopAsync(run);

        connection.Subscriptions.Select(s => s.Securities.Count).ShouldBe([100, 100, 30]);
        connection.Subscriptions.ShouldAllBe(s => s.Mode == FeedMode.Quote);
        world.Store.Events.Select(e => e.Event.Kind).ShouldContain(OpeningBarsEvent.Started);
        world.Store.Events.ShouldAllBe(e => e.Day == Day);
    }

    [Fact]
    public async Task Packets_become_one_complete_bar_from_exchange_time_written_once_and_the_next_minute_is_still_open()
    {
        var world = new World();
        var run = world.Start();
        var connection = await world.ConnectedAsync();
        await world.StepAsync(TimeSpan.FromSeconds(75)); // 09:15:15

        connection.Push(World.Quote(1001, At(9, 15, 1), 100m, 1_000));
        connection.Push(World.Quote(1001, At(9, 15, 40), 102m, 1_500));
        connection.Push(World.Quote(1001, At(9, 15, 59), 99m, 2_000));
        connection.Push(World.Quote(1001, At(9, 16, 3), 101m, 2_600));
        await UntilAsync(() => world.Recorder.PacketsRead == 4, "packets not read");
        await world.StepAsync(TimeSpan.FromSeconds(2));
        await UntilAsync(() => world.Store.Bars.Count == 1, "the bar was not written");
        var result = await world.StopAsync(run);

        world.Store.Bars.Single().ShouldBe(new MinuteBar(IdA, At(9, 15), 100m, 102m, 99m, 99m, 2_000, 3));
        result.Stats.Bars.ShouldBe(1);
        result.Stats.Duplicates.ShouldBe(0);
    }

    [Fact]
    public async Task A_minute_that_is_stored_already_is_a_duplicate_and_is_counted_not_written_twice()
    {
        var world = new World();
        world.Store.Bars.Add(new MinuteBar(IdA, At(9, 15), 1m, 1m, 1m, 1m, 1, 1));
        var run = world.Start();
        var connection = await world.ConnectedAsync();
        await world.StepAsync(TimeSpan.FromSeconds(75));

        connection.Push(World.Quote(1001, At(9, 15, 5), 100m, 1_000));
        connection.Push(World.Quote(1001, At(9, 16, 5), 101m, 1_500));
        await UntilAsync(() => world.Recorder.PacketsRead == 2, "packets not read");
        await world.StepAsync(TimeSpan.FromSeconds(2));
        await UntilAsync(() => world.Store.AppendCalls >= 1, "nothing written");
        var result = await world.StopAsync(run);

        world.Store.Bars.Count.ShouldBe(1);
        world.Store.Bars.Single().Open.ShouldBe(1m);
        result.Stats.Duplicates.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_write_keeps_the_bar_queued_and_the_next_second_writes_it()
    {
        var world = new World();
        var run = world.Start();
        var connection = await world.ConnectedAsync();
        await world.StepAsync(TimeSpan.FromSeconds(75));
        world.Store.FailAppends = 1;

        connection.Push(World.Quote(1001, At(9, 15, 5), 100m, 1_000));
        connection.Push(World.Quote(1001, At(9, 16, 5), 101m, 1_500));
        await UntilAsync(() => world.Recorder.PacketsRead == 2, "packets not read");
        await world.StepAsync(TimeSpan.FromSeconds(3));
        await UntilAsync(() => world.Store.Bars.Count == 1, "the bar never went in");
        await world.StopAsync(run);

        world.Store.AppendCalls.ShouldBeGreaterThanOrEqualTo(2);
        world.Store.Bars.Count.ShouldBe(1);
    }

    [Fact]
    public async Task At_the_record_until_time_it_unsubscribes_closes_and_ends_as_until()
    {
        var world = new World(9, 30);
        var run = world.Start();
        var connection = await world.ConnectedAsync();

        await world.StepAsync(TimeSpan.FromSeconds(62)); // 09:31:02
        var result = await run;

        result.End.ShouldBe(OpeningSessionEnd.Until);
        connection.Unsubscribes.ShouldBeGreaterThan(0);
        connection.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Free_space_under_the_floor_stops_it_and_records_low_disk()
    {
        var world = new World(9, 14, OpeningBarsSettings.Off with { Enabled = true, MinFreeBytes = 10, Silence = TimeSpan.FromHours(1) });
        world.Store.Free = 5;
        var run = world.Start();
        var connection = await world.ConnectedAsync();

        await world.StepAsync(TimeSpan.FromSeconds(2));
        var result = await run;

        result.End.ShouldBe(OpeningSessionEnd.LowDisk);
        world.Store.Events.Select(e => e.Event.Kind).ShouldContain(OpeningBarsEvent.LowDisk);
        connection.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_drop_is_a_gap_event_from_the_drop_to_the_reconnect_and_the_second_start_is_recorded()
    {
        var world = new World();
        var run = world.Start();
        var first = await world.ConnectedAsync();

        first.Close();
        await UntilAsync(() => first.Disposed, "the first connection was not left");
        for (var i = 0; i < 30 && world.Connector.Connections.Count < 2; i++)
        {
            await world.StepAsync(TimeSpan.FromSeconds(1));
        }

        world.Connector.Connections.Count.ShouldBe(2);
        await UntilAsync(() => world.Store.Events.Count(e => e.Event.Kind == OpeningBarsEvent.Started) == 2 && world.Store.Events.Any(e => e.Event.Kind == OpeningBarsEvent.Gap), "events not written");
        await world.StopAsync(run);

        var gaps = world.Store.Events.Where(e => e.Event.Kind == OpeningBarsEvent.Gap).Select(e => e.Event).ToList();
        gaps.Count.ShouldBe(2, "the open gap is on record from the drop, and a closing row follows");
        gaps[0].To.ShouldBeNull();
        gaps[1].From.ShouldBe(gaps[0].From);
        gaps[1].To.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_connection_wide_silence_above_the_limit_is_a_gap_that_is_left_open_when_the_run_ends()
    {
        var world = new World(9, 14, OpeningBarsSettings.Off with { Enabled = true, Silence = TimeSpan.FromSeconds(30) });
        var run = world.Start();
        await world.ConnectedAsync();

        await world.StepAsync(TimeSpan.FromSeconds(120)); // 09:16:00: silent since the open
        await world.StopAsync(run);

        var gap = world.Store.Events.Single(e => e.Event.Kind == OpeningBarsEvent.Gap).Event;
        gap.From.ShouldBe(At(9, 15));
        gap.To.ShouldBeNull();
    }

    [Fact]
    public async Task A_silence_is_on_record_while_it_lasts_and_the_minute_it_overlaps_is_never_written_as_complete()
    {
        var world = new World(9, 14, OpeningBarsSettings.Off with { Enabled = true, Silence = TimeSpan.FromSeconds(30) });
        var run = world.Start();
        var connection = await world.ConnectedAsync();
        await world.StepAsync(TimeSpan.FromSeconds(70)); // 09:15:10
        connection.Push(World.Quote(1001, At(9, 15, 5), 100m, 1_000));
        await UntilAsync(() => world.Recorder.PacketsRead == 1, "packet not read");

        await world.StepAsync(TimeSpan.FromSeconds(75)); // 09:16:25: silent from 09:15:10; the 09:15 minute is over by the grace
        await UntilAsync(() => world.Store.Events.Any(e => e.Event.Kind == OpeningBarsEvent.Gap && e.Event.To is null), "the open gap was not on record while it lasts");
        await world.StopAsync(run);

        world.Store.Bars.ShouldNotContain(b => b.MinuteStart == At(9, 15), "a minute a gap overlaps is not complete");
    }

    [Fact]
    public async Task A_median_skew_over_five_minutes_between_exchange_and_receive_time_stops_the_run_and_records_clock_skew()
    {
        var world = new World();
        var run = world.Start();
        var connection = await world.ConnectedAsync();
        await world.StepAsync(TimeSpan.FromSeconds(75));

        for (var i = 0; i < 12; i++)
        {
            connection.Push(World.Quote(1001, At(14, 45, i), 100m, 1_000 + i)); // IST-as-UTC would put 09:15 at 14:45
        }

        var result = await run;

        result.End.ShouldBe(OpeningSessionEnd.ClockSkew);
        Math.Abs(result.Stats.SkewSeconds).ShouldBeGreaterThan(300);
        world.Store.Events.Select(e => e.Event.Kind).ShouldContain(OpeningBarsEvent.ClockSkew);
        result.Stats.Outside.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(806)]
    [InlineData(805)]
    public async Task A_disconnect_that_says_the_plan_or_the_connection_count_does_not_allow_it_ends_the_session_as_refused_without_reconnecting(int code)
    {
        var world = new World();
        var run = world.Start();
        var connection = await world.ConnectedAsync();

        connection.Push(new FeedPacket(FeedPacketKind.Disconnect, default, Reason: code));
        var result = await run;

        result.End.ShouldBe(OpeningSessionEnd.Refused);
        world.Connector.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_token_ends_it_and_no_credentials_end_it_as_no_token()
    {
        var refused = new World();
        refused.Connector.Scripted.Enqueue(FeedConnectResult.Failed(FeedConnectFailureKind.TokenRefused, "refused"));
        var none = new World();
        none.Credentials.Token = null;

        var refusedResult = await refused.Start();
        var noneResult = await none.Start();

        refusedResult.End.ShouldBe(OpeningSessionEnd.TokenRefused);
        noneResult.End.ShouldBe(OpeningSessionEnd.NoToken);
        none.Connector.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_failure_of_the_connector_is_a_failed_attempt_not_an_exception_out_of_the_session()
    {
        var world = new World();
        world.Connector.Scripted.Enqueue(FeedConnectResult.Failed(FeedConnectFailureKind.Failed, "down"));
        var run = world.Start();

        for (var i = 0; i < 30 && world.Connector.Connections.Count < 1; i++)
        {
            await world.StepAsync(TimeSpan.FromSeconds(1));
        }

        var result = await world.StopAsync(run);

        world.Connector.Calls.ShouldBeGreaterThanOrEqualTo(2);
        result.End.ShouldBe(OpeningSessionEnd.Stopped);
        world.Store.Events.Where(e => e.Event.Kind == OpeningBarsEvent.Gap).ShouldNotBeEmpty("the time the connection was down is a gap");
    }
}
