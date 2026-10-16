using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Market;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Portfolio.Live;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Live;

/// <summary>A session the test controls: it runs until the test finishes it (or it is cancelled, which ends it as stopped).</summary>
internal sealed class FakeFeedSession : IFeedSession
{
    private TaskCompletionSource<FeedSessionResult>? _current;

    public string State { get; set; } = FeedStates.Connected;

    public int Starts { get; private set; }

    /// <summary>A session that does not end when it is cancelled (a connection that hangs).</summary>
    public bool IgnoreCancel { get; set; }

    public bool Running => _current is { Task.IsCompleted: false };

    public Task<FeedSessionResult> RunAsync(CancellationToken cancellationToken)
    {
        Starts++;
        var current = new TaskCompletionSource<FeedSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = current;
        cancellationToken.Register(() =>
        {
            if (!IgnoreCancel)
            {
                current.TrySetResult(new FeedSessionResult(FeedSessionEnd.Stopped));
            }
        });
        return current.Task;
    }

    public void Finish(FeedSessionEnd end, string? fingerprint = null) => _current!.TrySetResult(new FeedSessionResult(end, fingerprint));

    public void Crash() => _current!.TrySetException(new InvalidOperationException("boom"));
}

internal sealed class FakeFeedHeartbeats : IFeedHeartbeats
{
    public List<(string State, FeedHeartbeat Details, string Instance)> Reports { get; } = [];

    public ValueTask ReportAsync(string state, FeedHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        Reports.Add((state, details, instance));
        return ValueTask.CompletedTask;
    }

    public FeedStatus? Status { get; set; }

    public ValueTask<FeedStatus?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Status);
}

internal sealed class FakeManualRun : IFeedManualRun
{
    public DateTimeOffset? Until { get; set; }

    public string? Actor { get; private set; }

    public ValueTask<DateTimeOffset?> GetManualUntilAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Until);

    public ValueTask ClearAsync(string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Until is not null)
        {
            (Until, Actor) = (now, actor);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SetManualUntilAsync(DateTimeOffset until, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        (Until, Actor) = (until, actor);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeCalendar : ITradingCalendar
{
    public TradingCalendar Calendar { get; set; } = new();

    public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Calendar);

    public void Invalidate()
    {
        // nothing is kept
    }
}

/// <summary>The supervisor over fakes: the rows and runs of the services framework in memory, a session the test finishes, the heartbeat, the manual-run marker, the Data account and a clock (Monday 2026-10-12, 08:00 India time).</summary>
internal sealed class SupervisorWorld
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public SupervisorWorld(int hour = 8, int minute = 0, int day = 12)
    {
        Clock = new SettableClock(At(day, hour, minute));
        var services = new MiniProvider()
            .Singleton(new ServiceRegistry([]))
            .Singleton<IServiceScheduleStore>(Data)
            .Singleton<IServiceRunStore>(Data)
            .Singleton<IFeedHeartbeats>(Heartbeats)
            .Singleton<IFeedManualRun>(Manual)
            .Singleton<ILiveQuotes>(Quotes)
            .Singleton<IDataAccountCredentials>(Credentials)
            .Singleton<ITradingCalendar>(Calendar)
            .Scoped<ServiceSettingsReader>();
        Supervisor = new LiveFeedSupervisor(services, Session, Metrics, new FakeApplicationInfo(), Clock, Log);
    }

    public SettableClock Clock { get; }

    public InMemoryServiceData Data { get; } = new();

    public FakeFeedSession Session { get; } = new();

    public FakeFeedHeartbeats Heartbeats { get; } = new();

    public FakeManualRun Manual { get; } = new();

    public FakeLiveQuotes Quotes { get; } = new();

    public FakeCredentials Credentials { get; } = new();

    public FakeCalendar Calendar { get; } = new();

    public FeedMetrics Metrics { get; } = new();

    public CapturingLogger<LiveFeedSupervisor> Log { get; } = new();

    public LiveFeedSupervisor Supervisor { get; }

    public List<ServiceRun> Runs => [.. Data.RunsOf(LiveFeedService.Id)];

    public string LastState => Heartbeats.Reports[^1].State;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, Ist);

    public void SetClock(DateTimeOffset instant) => Clock.Jump(instant);

    public Task TickAsync() => Supervisor.TickAsync(Ct);

    /// <summary>Waits until the session task the supervisor started has ended.</summary>
    public static async Task UntilEndedAsync(Task? task)
    {
        for (var i = 0; i < 1_000_000 && task is { IsCompleted: false }; i++)
        {
            await Task.Yield();
        }

        (task is { IsCompleted: true }).ShouldBeTrue("the session did not end");
    }
}
