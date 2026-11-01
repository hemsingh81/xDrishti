using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.OptionChain;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Depth;
using XDrishti.Application.Tests.Live;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.OptionChain.ChainSamples;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>
/// The option-chain supervisor over fakes (a Monday, 2026-10-12): the service rows and runs in memory, a session the test finishes, the owner's switch, the store, the heartbeat, the targets, the Data account and a clock.
/// <b>No chain client is registered</b>: a supervisor that sent a request itself would fail the look.
/// </summary>
internal sealed class OptionChainSupervisorWorld
{
    public OptionChainSupervisorWorld(int hour = 9, int minute = 20, bool deployment = true, bool switchOn = true, bool serviceOn = true)
    {
        Clock = new SettableClock(At(12, hour, minute));
        Settings = OptionChainSettings.Off with { Enabled = deployment };
        if (switchOn)
        {
            Switch.Set(true);
        }

        if (serviceOn)
        {
            Data.Store(OptionChainRecorderService.Id, OptionChainRecorderService.Definition.Defaults with { Enabled = true });
        }

        var services = new MiniProvider()
            .Singleton(new ServiceRegistry([]))
            .Singleton<IServiceScheduleStore>(Data)
            .Singleton<IServiceRunStore>(Data)
            .Singleton<IOptionChainHeartbeats>(Heartbeats)
            .Singleton<IDataAccountCredentials>(Credentials)
            .Singleton<XDrishti.Application.Abstractions.Market.Calendar.ITradingCalendar>(Calendar)
            .Singleton<IOptionChainSwitchStore>(Switch)
            .Singleton<IOptionChainStore>(Store)
            .Singleton<IChainTargets>(Targets)
            .Scoped<ServiceSettingsReader>();
        Supervisor = new OptionChainSupervisor(services, Session, Settings, new FakeApplicationInfo(), Clock, NullLogger<OptionChainSupervisor>.Instance);
    }

    public SettableClock Clock { get; }

    public OptionChainSettings Settings { get; }

    public InMemoryServiceData Data { get; } = new();

    public FakeChainSession Session { get; } = new();

    public FakeChainHeartbeats Heartbeats { get; } = new();

    public CountingCredentials Credentials { get; } = new();

    public FakeCalendar Calendar { get; } = new();

    public FakeChainSwitchStore Switch { get; } = new();

    public FakeChainStore Store { get; } = new();

    public FakeChainTargets Targets { get; } = new();

    public OptionChainSupervisor Supervisor { get; }

    public List<ServiceRun> Runs => [.. Data.RunsOf(OptionChainRecorderService.Id)];

    public string LastState => Heartbeats.Reports[^1].State;

    public static DateOnly Today => new(2026, 10, 12);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
