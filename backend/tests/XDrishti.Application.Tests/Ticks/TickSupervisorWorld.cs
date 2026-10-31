using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Depth;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Ticks;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>
/// The tick supervisor over fakes (a Monday, 2026-10-12): the service rows and runs in memory, a session the test finishes, the owner's tick switch, the tick store (with the depth recorder's stored sets, read only), the heartbeat, the Data account
/// and a clock. <b>No depth store is registered</b>: a tick supervisor that asked for one would fail the look.
/// </summary>
internal sealed class TickSupervisorWorld
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public TickSupervisorWorld(int hour = 9, int minute = 20, bool deployment = true, bool switchOn = true, bool serviceOn = true, bool depthSetStored = true)
    {
        Clock = new SettableClock(At(12, hour, minute));
        Settings = TickSettings.Off with { Enabled = deployment };
        Depth = DepthSettings.Off with { PinnedSymbols = ["PINNED"] };
        if (switchOn)
        {
            Switch.Set(true);
        }

        if (serviceOn)
        {
            Data.Store(TickRecorderService.Id, TickRecorderService.Definition.Defaults with { Enabled = true });
        }

        Securities.Known["PINNED"] = new InstrumentSecurity("PINNED", Guid.NewGuid(), "1111");
        Securities.Known["ARMED"] = new InstrumentSecurity("ARMED", Guid.NewGuid(), "2222");
        Securities.Known["LIQUID"] = new InstrumentSecurity("LIQUID", Guid.NewGuid(), "3333");
        Universe.Rows.Add(new DepthTurnover(Securities.Known["LIQUID"], 1_000_000));
        Armed.Tickets.Add(new ArmedTicket(1, 1, 1, "ARMED"));
        if (depthSetStored)
        {
            Store.DepthSets[Today] = [new TickSetEntry(Guid.NewGuid(), 4444, "DEPTH1", TickSources.DepthSet, "pinned", 1), new TickSetEntry(Guid.NewGuid(), 5555, "DEPTH2", TickSources.DepthSet, "turnover", 2)];
        }

        var services = new MiniProvider()
            .Singleton(new ServiceRegistry([]))
            .Singleton<IServiceScheduleStore>(Data)
            .Singleton<IServiceRunStore>(Data)
            .Singleton<ITickHeartbeats>(Heartbeats)
            .Singleton<IDataAccountCredentials>(Credentials)
            .Singleton<ITradingCalendar>(Calendar)
            .Singleton<ITickSwitchStore>(Switch)
            .Singleton<ITickStore>(Store)
            .Singleton<IArmedTicketReader>(Armed)
            .Singleton<IInstrumentSecurities>(Securities)
            .Singleton<IDepthUniverse>(Universe)
            .Scoped<ServiceSettingsReader>();
        Supervisor = new TickRecorderSupervisor(services, Session, Settings, Depth, new FakeApplicationInfo(), Clock, NullLogger<TickRecorderSupervisor>.Instance);
    }

    public SettableClock Clock { get; }

    public TickSettings Settings { get; }

    public DepthSettings Depth { get; }

    public InMemoryServiceData Data { get; } = new();

    public FakeTickSession Session { get; } = new();

    public FakeTickHeartbeats Heartbeats { get; } = new();

    public CountingCredentials Credentials { get; } = new();

    public FakeCalendar Calendar { get; } = new();

    public FakeTickSwitchStore Switch { get; } = new();

    public FakeTickStore Store { get; } = new();

    public FakeArmedTickets Armed { get; } = new();

    public FakeSecurities Securities { get; } = new();

    public FakeDepthUniverse Universe { get; } = new();

    public TickRecorderSupervisor Supervisor { get; }

    public List<ServiceRun> Runs => [.. Data.RunsOf(TickRecorderService.Id)];

    public string LastState => Heartbeats.Reports[^1].State;

    public static DateOnly Today => new(2026, 10, 12);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, Ist);

    public void SetClock(DateTimeOffset instant) => Clock.Jump(instant);

    public Task TickAsync() => Supervisor.TickAsync(Ct);

    public static async Task UntilEndedAsync(Task? task)
    {
        for (var i = 0; i < 1_000_000 && task is { IsCompleted: false }; i++)
        {
            await Task.Yield();
        }

        (task is { IsCompleted: true }).ShouldBeTrue("the session did not end");
    }
}
