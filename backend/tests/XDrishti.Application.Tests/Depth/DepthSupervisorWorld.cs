using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Depth;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Depth;

/// <summary>Credentials that count how often they are read (the gates must keep this at zero).</summary>
internal sealed class CountingCredentials : IDataAccountCredentials
{
    private readonly FakeCredentials _inner = new();

    public int Reads { get; private set; }

    public string? Token
    {
        get => _inner.Token;
        set => _inner.Token = value;
    }

    public ValueTask<DataAccountCredentials?> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return _inner.GetCredentialsAsync(cancellationToken);
    }
}

/// <summary>The depth supervisor over fakes (a Monday, 2026-10-12): the service rows and runs in memory, a session the test finishes, the owner's switch, the store, the heartbeat, the Data account and a clock.</summary>
internal sealed class DepthSupervisorWorld
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public DepthSupervisorWorld(int hour = 9, int minute = 20, bool deployment = true, bool switchOn = true, bool serviceOn = true, DepthSettings? settings = null)
    {
        Clock = new SettableClock(At(12, hour, minute));
        Settings = settings ?? DepthSettings.Off with { Enabled = deployment, PinnedSymbols = ["PINNED"] };
        if (switchOn)
        {
            Switch.Set(true);
        }

        if (serviceOn)
        {
            Data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = true });
        }

        Securities.Known["PINNED"] = new InstrumentSecurity("PINNED", Guid.NewGuid(), "1111");
        Securities.Known["ARMED"] = new InstrumentSecurity("ARMED", Guid.NewGuid(), "2222");
        Securities.Known["LIQUID"] = new InstrumentSecurity("LIQUID", Guid.NewGuid(), "3333");
        Universe.Rows.Add(new DepthTurnover(Securities.Known["LIQUID"], 1_000_000));
        Armed.Tickets.Add(new ArmedTicket(1, 1, 1, "ARMED"));
        var services = new MiniProvider()
            .Singleton(new ServiceRegistry([]))
            .Singleton<IServiceScheduleStore>(Data)
            .Singleton<IServiceRunStore>(Data)
            .Singleton<IDepthHeartbeats>(Heartbeats)
            .Singleton<IDataAccountCredentials>(Credentials)
            .Singleton<ITradingCalendar>(Calendar)
            .Singleton<IDepthSwitchStore>(Switch)
            .Singleton<IDepthStore>(Store)
            .Singleton<IArmedTicketReader>(Armed)
            .Singleton<IInstrumentSecurities>(Securities)
            .Singleton<IDepthUniverse>(Universe)
            .Scoped<ServiceSettingsReader>();
        Supervisor = new DepthRecorderSupervisor(services, Session, Settings, new FakeApplicationInfo(), Clock, NullLogger<DepthRecorderSupervisor>.Instance);
    }

    public SettableClock Clock { get; }

    public DepthSettings Settings { get; }

    public InMemoryServiceData Data { get; } = new();

    public FakeDepthSession Session { get; } = new();

    public FakeDepthHeartbeats Heartbeats { get; } = new();

    public CountingCredentials Credentials { get; } = new();

    public FakeCalendar Calendar { get; } = new();

    public FakeDepthSwitchStore Switch { get; } = new();

    public FakeDepthStore Store { get; } = new();

    public FakeArmedTickets Armed { get; } = new();

    public FakeSecurities Securities { get; } = new();

    public FakeDepthUniverse Universe { get; } = new();

    public DepthRecorderSupervisor Supervisor { get; }

    public List<ServiceRun> Runs => [.. Data.RunsOf(DepthRecorderService.Id)];

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
