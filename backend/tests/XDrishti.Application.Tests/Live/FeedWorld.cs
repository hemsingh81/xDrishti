using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Live;
using XDrishti.Application.Tests.Market;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Tests.Portfolio.Live;

namespace XDrishti.Application.Tests.Live;

/// <summary>
/// The connection manager wired over fakes: a clock (Monday 2026-10-12, 10:00 India time, in market hours), a connector that hands out in-memory connections, the active set from a provider the test edits, a sink, a listener, credentials.
/// Time is moved by the test: <see cref="RunClockUntilAsync"/> advances the clock in steps while the manager works on the thread pool.
/// </summary>
internal sealed class FeedWorld : IDisposable
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly CancellationTokenSource _stop = new();
    private Task<FeedSessionResult>? _run;

    public FeedWorld(DateTimeOffset? start = null, LiveFeedSettings? settings = null, Func<double>? random = null)
    {
        Clock = new SettableClock(start ?? new DateTimeOffset(2026, 10, 12, 10, 0, 0, Ist));
        Settings = settings ?? LiveFeedSettings.Default with { SubscribeSpacing = TimeSpan.Zero };
        Connector = new FakeFeedConnector(Clock);
        var services = new MiniProvider()
            .Singleton<ILiveQuotes>(Quotes)
            .Singleton<IDataAccountCredentials>(Credentials)
            .ScopedInstance<IActiveSetProvider>(Provider);
        Manager = new FeedConnectionManager(
            services,
            Connector,
            new ActiveSetLoader(services, Settings, NullLogger<ActiveSetLoader>.Instance),
            Changes,
            Flush,
            Metrics,
            new ReconnectBackoff(random ?? (() => 0d)),
            Settings,
            Clock,
            Log);
    }

    public SettableClock Clock { get; }

    public LiveFeedSettings Settings { get; }

    public FakeActiveSetProvider Provider { get; } = new();

    public FakeCredentials Credentials { get; } = new();

    public FakeLiveQuotes Quotes { get; } = new();

    public FakeFeedConnector Connector { get; }

    public FakeLiveFlush Flush { get; } = new();

    public FakeActiveSetChanges Changes { get; } = new();

    public FeedMetrics Metrics { get; } = new();

    public CapturingLogger<FeedConnectionManager> Log { get; } = new();

    public FeedConnectionManager Manager { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<FeedSessionResult> Start() => _run ??= Task.Run(() => Manager.RunAsync(_stop.Token), Ct);

    /// <summary>Starts the session in the background.</summary>
    public void Begin() => _ = Start();

    /// <summary>Waits for the session to end by itself.</summary>
    public Task<FeedSessionResult> ResultAsync() => Start().WaitAsync(TimeSpan.FromSeconds(30), Ct);

    public async Task<FeedSessionResult> StopAsync()
    {
        await _stop.CancelAsync();
        return await ResultAsync();
    }

    /// <summary>Waits (without moving the clock) until the condition holds.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 1_000_000 && !condition(); i++)
        {
            Ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        condition().ShouldBeTrue("the condition was not reached");
    }

    /// <summary>Moves the clock forward in steps, giving the manager a turn after each, until the condition holds.</summary>
    public async Task RunClockUntilAsync(Func<bool> condition, TimeSpan step)
    {
        for (var i = 0; i < 100_000 && !condition(); i++)
        {
            Ct.ThrowIfCancellationRequested();
            Clock.Advance(step);
            for (var turn = 0; turn < 20 && !condition(); turn++)
            {
                await Task.Yield();
            }
        }

        condition().ShouldBeTrue("the condition was not reached while the clock ran");
    }

    /// <summary>Starts the session and waits until it is subscribed and streaming (state connected or idle).</summary>
    public async Task StartConnectedAsync()
    {
        Begin();
        await UntilAsync(() => Manager.State is FeedStates.Connected or FeedStates.Idle && Changes.Watching);
    }

    public void Dispose() => _stop.Dispose();
}
