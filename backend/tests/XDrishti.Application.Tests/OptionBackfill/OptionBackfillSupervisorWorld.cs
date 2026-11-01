using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.OptionBackfill;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Depth;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Application.Tests.OptionBackfill.BackfillSamples;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>
/// The backfill supervisor over fakes (a Monday evening, 2026-10-12): the service rows and runs in memory, a session the test finishes, the owner's switch and acceptance, the store, the heartbeat, the targets, the Data account and a clock.
/// <b>No backfill client is registered</b>: a supervisor that sent a request itself would fail the look.
/// </summary>
internal sealed class OptionBackfillSupervisorWorld
{
    public OptionBackfillSupervisorWorld(int hour = 20, int minute = 0, bool deployment = true, bool switchOn = true, bool serviceOn = true, bool accepted = true)
    {
        Clock = new SettableClock(At(12, hour, minute));
        Settings = Small with { Enabled = deployment };
        if (switchOn)
        {
            Switch.Set(true);
        }

        if (accepted)
        {
            Ack.Accept();
        }

        if (serviceOn)
        {
            Data.Store(OptionBackfillService.Id, OptionBackfillService.Definition.Defaults with { Enabled = true });
        }

        var services = new MiniProvider()
            .Singleton(new ServiceRegistry([]))
            .Singleton<TimeProvider>(Clock)
            .Singleton(Settings)
            .Singleton<IServiceScheduleStore>(Data)
            .Singleton<IServiceRunStore>(Data)
            .Singleton<IOptionBackfillHeartbeats>(Heartbeats)
            .Singleton<IDataAccountCredentials>(Credentials)
            .Singleton<IOptionBackfillSwitchStore>(Switch)
            .Singleton<IBackfillProbeAckStore>(Ack)
            .Singleton<IRollingBarStore>(Store)
            .Singleton<IBackfillFetchLog>(Store)
            .Singleton<IBackfillTargets>(Targets)
            .Scoped<ServiceSettingsReader>()
            .Scoped<BackfillProgressReader>();
        Supervisor = new OptionBackfillSupervisor(services, Session, Settings, new FakeApplicationInfo(), Clock, NullLogger<OptionBackfillSupervisor>.Instance);
    }

    public SettableClock Clock { get; }

    public OptionBackfillSettings Settings { get; }

    public InMemoryServiceData Data { get; } = new();

    public FakeBackfillSession Session { get; } = new();

    public FakeBackfillHeartbeats Heartbeats { get; } = new();

    public CountingCredentials Credentials { get; } = new();

    public FakeBackfillSwitchStore Switch { get; } = new();

    public FakeProbeAckStore Ack { get; } = new();

    public FakeBackfillStore Store { get; } = new();

    public FakeBackfillTargets Targets { get; } = new();

    public OptionBackfillSupervisor Supervisor { get; }

    public List<ServiceRun> Runs => [.. Data.RunsOf(OptionBackfillService.Id)];

    public string LastState => Heartbeats.Reports[^1].State;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void SetClock(DateTimeOffset instant) => Clock.Jump(instant);

    public Task TickAsync() => Supervisor.TickAsync(Ct);

    public static async Task UntilEndedAsync(Task? task)
    {
        await (task ?? throw new InvalidOperationException("the session did not start")).WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }
}
