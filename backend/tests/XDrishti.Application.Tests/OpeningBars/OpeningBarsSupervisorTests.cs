using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.OpeningBars;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Market.Baskets.Derived;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;
using LiveCalendar = XDrishti.Application.Tests.Live.FakeCalendar;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>
/// The opening-minute recorder's run owner over fakes (BK-03b criteria 1, 5, 7, 9, 10, 11, 12), on Monday 2026-10-12: each of the three gates alone keeps credentials, sockets and tables untouched; a holiday starts nothing and asks nothing; the freeze is asked from
/// 09:20 once the deployment flag is on, whatever the other gates say, and is not asked again once nothing waits; the set is the capped universe; the run record carries the day's counts.
/// </summary>
public sealed class OpeningBarsSupervisorTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeFreeze : ICommandHandler<FreezeOpeningBasketsCommand, OpeningFreezeRun>
    {
        public List<DateOnly> Calls { get; } = [];

        public int Waiting { get; set; } = 1;

        public bool Throw { get; set; }

        public ValueTask<Result<OpeningFreezeRun>> HandleAsync(FreezeOpeningBasketsCommand command, CancellationToken cancellationToken)
        {
            Calls.Add(command.Day);
            if (Throw)
            {
                throw new InvalidOperationException("the freeze failed");
            }

            return ValueTask.FromResult(Result.Success(new OpeningFreezeRun(Waiting, [])));
        }
    }

    private sealed class FakeSecuritiesByKey : IInstrumentSecurities
    {
        public Dictionary<string, InstrumentSecurity> Known { get; } = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Asked { get; private set; } = [];

        public ValueTask<IReadOnlyDictionary<string, InstrumentSecurity>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken)
        {
            Asked = canonicalSymbols;
            return ValueTask.FromResult<IReadOnlyDictionary<string, InstrumentSecurity>>(Known.Where(k => canonicalSymbols.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value));
        }
    }

    private sealed class World
    {
        public World(int day = 12, int hour = 9, int minute = 12, bool deployment = true, bool switchOn = true, bool serviceOn = true, bool basketOn = true, int instruments = 2, int cap = 500, long minFree = 0)
        {
            Clock = new SettableClock(new DateTimeOffset(2026, 10, day, hour, minute, 0, Ist));
            Settings = OpeningBarsSettings.Off with { Enabled = deployment, MaxInstruments = cap, MinFreeBytes = minFree, Silence = TimeSpan.FromHours(1) };
            Switch.On = switchOn;
            if (serviceOn)
            {
                Data.Store(OpeningBarsService.Id, OpeningBarsService.Definition.Defaults with { Enabled = true });
            }

            Versions.Definitions.Add(new DerivedDefinitionRow(Guid.NewGuid(), "Derived: Gappers", 4, basketOn, null, new Dictionary<string, double>()));
            for (var i = 0; i < instruments; i++)
            {
                var instrument = new UniverseInstrument(Guid.NewGuid(), new InstrumentBars($"S{i}", DerivedWorld.Weekdays(new DateOnly(2026, 10, 9), 25, 1_000 * (i + 1))));
                Universe.Instruments.Add(instrument);
                Securities.Known["NSE:" + instrument.Bars.Symbol] = new InstrumentSecurity("NSE:" + instrument.Bars.Symbol, instrument.InstrumentId, (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var services = new MiniProvider()
                .Singleton(new ServiceRegistry([]))
                .Singleton<IServiceScheduleStore>(Data)
                .Singleton<IServiceRunStore>(Data)
                .Singleton<IDataAccountCredentials>(Credentials)
                .Singleton<ITradingCalendar>(Calendar)
                .ScopedInstance<IOpeningBarsSwitchStore>(Switch)
                .ScopedInstance<IOpeningBarsStore>(Store)
                .ScopedInstance<IDerivedSettings>(Derived)
                .ScopedInstance<IDerivedVersionStore>(Versions)
                .ScopedInstance<IDerivedUniverseReader>(Universe)
                .ScopedInstance<IInstrumentSecurities>(Securities)
                .ScopedInstance<ICommandHandler<FreezeOpeningBasketsCommand, OpeningFreezeRun>>(Freeze)
                .Scoped<ServiceSettingsReader>();
            Recorder = new OpeningBarsRecorder(services, Connector, Settings, Clock, NullLogger<OpeningBarsRecorder>.Instance);
            Supervisor = new OpeningBarsSupervisor(services, Recorder, Settings, Clock, NullLogger<OpeningBarsSupervisor>.Instance);
        }

        public SettableClock Clock { get; }

        public OpeningBarsSettings Settings { get; }

        public InMemoryServiceData Data { get; } = new();

        public CountingCredentials Credentials { get; } = new();

        public LiveCalendar Calendar { get; } = new();

        public FakeOpeningSwitch Switch { get; } = new();

        public FakeOpeningStore Store { get; } = new();

        public FakeDerivedSettings Derived { get; } = new();

        public FakeVersionStore Versions { get; } = new();

        public FakeUniverse Universe { get; } = new();

        public FakeSecuritiesByKey Securities { get; } = new();

        public FakeFreeze Freeze { get; } = new();

        public FakeOpeningConnector Connector { get; } = new();

        public OpeningBarsRecorder Recorder { get; }

        public OpeningBarsSupervisor Supervisor { get; }

        public Task TickAsync() => Supervisor.TickAsync(Ct);

        public async Task<FakeFeedConnection> ConnectedAsync()
        {
            for (var i = 0; i < 1_000_000 && !(Connector.Connections.Count > 0 && Connector.Connections[0].Subscriptions.Count > 0); i++)
            {
                await Task.Yield();
            }

            return Connector.Connections.Single();
        }

        public void SetClock(int hour, int minute, int second = 0) => Clock.Jump(new DateTimeOffset(2026, 10, 12, hour, minute, second, Ist));

        public List<ServiceRun> Runs => [.. Data.RunsOf(OpeningBarsService.Id)];
    }

    private static void NothingTouched(World world)
    {
        world.Credentials.Reads.ShouldBe(0);
        world.Connector.Calls.ShouldBe(0);
        world.Store.AppendCalls.ShouldBe(0);
        world.Store.Events.ShouldBeEmpty();
        world.Runs.ShouldBeEmpty();
    }

    // ---- criterion 1: the three gates, each alone ----

    [Fact]
    public async Task With_the_deployment_flag_off_nothing_is_read_opened_written_or_asked_not_even_the_freeze()
    {
        var world = new World(deployment: false, hour: 9, minute: 25);

        await world.TickAsync();

        NothingTouched(world);
        world.Freeze.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_owner_switch_off_no_credential_socket_or_row_is_touched()
    {
        var world = new World(switchOn: false);

        await world.TickAsync();

        NothingTouched(world);
    }

    [Fact]
    public async Task The_service_starts_paused_and_a_paused_service_keeps_the_recorder_off()
    {
        OpeningBarsService.Definition.Defaults.Enabled.ShouldBeFalse();
        var world = new World(serviceOn: false);

        await world.TickAsync();

        NothingTouched(world);
    }

    [Fact]
    public async Task Outside_the_service_window_nothing_starts()
    {
        var world = new World(hour: 9, minute: 5);

        await world.TickAsync();

        NothingTouched(world);
    }

    [Fact]
    public async Task With_no_frozen_intraday_basket_switched_on_there_is_nothing_to_record_and_no_connection_is_made()
    {
        var world = new World(basketOn: false);

        await world.TickAsync();

        world.Credentials.Reads.ShouldBe(0);
        world.Connector.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task With_all_three_gates_open_the_run_starts_with_the_capped_set_ordered_by_liquidity()
    {
        var world = new World(instruments: 3, cap: 50);

        await world.TickAsync();
        var connection = await world.ConnectedAsync();

        connection.ShouldNotBeNull();
        world.Connector.Calls.ShouldBe(1);
        world.Runs.Single().Status.ShouldBe(ServiceRunStatus.Running);
        world.Securities.Asked.ShouldBe(["NSE:S2", "NSE:S1", "NSE:S0"], "the most liquid by ADV20 first");
    }

    [Fact]
    public async Task Closing_a_gate_while_it_runs_stops_it_and_the_run_is_closed_with_the_days_counts()
    {
        var world = new World();
        await world.TickAsync();
        await world.ConnectedAsync();

        world.Switch.On = false;
        await world.TickAsync();

        var run = world.Runs.Single();
        run.Status.ShouldBe(ServiceRunStatus.Succeeded);
        run.Message.ShouldNotBeNull().ShouldContain("instruments");
        run.Message.ShouldContain("missing minutes");
        run.Message.ShouldContain("gaps");
    }

    // ---- criterion 8: retention ----

    [Fact]
    public async Task The_retention_runs_once_a_day_with_the_keep_days_even_on_a_weekend_and_never_with_the_deployment_flag_off()
    {
        var world = new World(day: 10, hour: 9, minute: 25); // a Saturday
        var off = new World(deployment: false);

        await world.TickAsync();
        await world.TickAsync();
        await off.TickAsync();

        world.Store.Prunes.ShouldBe([60]);
        off.Store.Prunes.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_retention_waits_for_all_three_gates_a_closed_switch_or_a_paused_service_prunes_nothing()
    {
        var switchOff = new World(switchOn: false);
        var paused = new World(serviceOn: false);

        await switchOff.TickAsync();
        await paused.TickAsync();

        switchOff.Store.Prunes.ShouldBeEmpty();
        paused.Store.Prunes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_free_space_that_cannot_be_read_counts_as_low_disk_never_a_pass()
    {
        var world = new World(minFree: 10);
        world.Store.Free = null;

        await world.TickAsync();

        world.Connector.Calls.ShouldBe(0);
        world.Store.Events.Select(e => e.Event.Kind).ShouldBe([OpeningBarsEvent.LowDisk]);
    }

    // ---- criterion 10: a non-trading day ----

    [Fact]
    public async Task On_a_weekend_there_is_no_connection_no_row_and_no_freeze()
    {
        var world = new World(day: 10, hour: 9, minute: 25);

        await world.TickAsync();

        NothingTouched(world);
        world.Freeze.Calls.ShouldBeEmpty();
    }

    // ---- criterion 5: the freeze is asked at the formation time ----

    [Fact]
    public async Task The_freeze_is_not_asked_before_0920_is_asked_from_then_and_not_again_once_nothing_waits()
    {
        var world = new World(switchOn: false); // the recorder is off; the freeze names no_live_bars itself
        world.SetClock(9, 19, 59);
        await world.TickAsync();
        world.Freeze.Calls.Count.ShouldBe(0);

        world.SetClock(9, 20, 8);
        await world.TickAsync();
        world.Freeze.Calls.ShouldBe([new DateOnly(2026, 10, 12)]);

        world.Freeze.Waiting = 0;
        world.SetClock(9, 30, 8);
        await world.TickAsync();
        await world.TickAsync();

        world.Freeze.Calls.Count.ShouldBe(2, "the call that found nothing waiting was the last");
    }

    [Fact]
    public async Task The_freeze_is_asked_even_while_the_owner_switch_and_the_service_are_off_so_a_day_without_a_recorder_is_named_not_formed()
    {
        var world = new World(switchOn: false, serviceOn: false, hour: 9, minute: 21);

        await world.TickAsync();

        world.Freeze.Calls.Count.ShouldBe(1);
        world.Credentials.Reads.ShouldBe(0);
    }

    // ---- criterion 9: low disk ----

    [Fact]
    public async Task Low_free_space_before_a_start_records_low_disk_once_and_opens_no_socket()
    {
        var world = new World(minFree: 10);
        world.Store.Free = 0;

        await world.TickAsync();
        await world.TickAsync();

        world.Connector.Calls.ShouldBe(0);
        world.Credentials.Reads.ShouldBe(0);
        world.Store.Events.Select(e => e.Event.Kind).ShouldBe([OpeningBarsEvent.LowDisk], "once for the day");
    }

    // ---- criterion 11: isolation ----

    [Fact]
    public async Task A_freeze_that_throws_is_logged_never_thrown_and_the_next_look_asks_again()
    {
        var world = new World(hour: 9, minute: 25);
        world.Freeze.Throw = true;

        await Should.NotThrowAsync(world.TickAsync);
        await Should.NotThrowAsync(world.TickAsync);

        world.Freeze.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public void The_service_is_a_window_service_09_10_to_09_32_run_by_the_feed_host()
    {
        OpeningBarsService.Definition.Defaults.Enabled.ShouldBeFalse();
        OpeningBarsService.RunsAs.ShouldBe("xd-feed");
        OpeningBarsService.WindowStart.ShouldBe(new TimeOnly(9, 10));
        OpeningBarsService.WindowEnd.ShouldBe(new TimeOnly(9, 32));
        OpeningBarsService.Definition.Id.ShouldBe("opening-bars");
    }
}
