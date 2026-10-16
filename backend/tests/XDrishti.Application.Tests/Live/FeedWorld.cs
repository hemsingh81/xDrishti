using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
/// Time is moved by the test, and never faster than the manager can follow: <see cref="RunClockUntilAsync"/> moves the clock from one wake-up of the manager to the next and waits until the manager has acted on each, whatever the
/// speed of the machine or of the process (a cold start takes milliseconds for what a warm one does in microseconds).
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

    /// <summary>
    /// How long a wait for the manager lasts before the test gives up. It limits a failure and is no delay: a wait that is met returns at once. It is a time and not a number of turns, because the speed of a turn depends on the
    /// machine and on the process (a cold process needs milliseconds for what a warm one does in microseconds) and the test projects run side by side.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Waits (without moving the clock) until the condition holds.</summary>
    public static Task UntilAsync(Func<bool> condition, string? because = null) => PollAsync(condition, because ?? "the condition was not reached");

    private static async Task PollAsync(Func<bool> condition, string because, Action? beforeEachTurn = null)
    {
        var reached = condition();
        var started = Stopwatch.GetTimestamp();
        while (!reached && Stopwatch.GetElapsedTime(started) < Patience)
        {
            Ct.ThrowIfCancellationRequested();
            beforeEachTurn?.Invoke();
            await Task.Yield();
            reached = condition(); // what was seen is what counts: asking again after the loop could meet a state that has moved on
        }

        reached.ShouldBeTrue(because);
    }

    /// <summary>What the manager has visibly done so far: only ever grows, so a change means it acted.</summary>
    private long Activity => Flush.Flushes + Connector.Attempts + Clock.TimersCreated + (_run is { IsCompleted: true } ? 1 : 0);

    /// <summary>
    /// Moves the clock, from one wake-up of the manager to the next, until the condition holds. Each move goes to the instant the earliest timer the manager waits on is due and no further, and the clock does not move again until
    /// the manager has shown a sign of life since (a flush, an attempt, a timer of its own). So the manager sees pass the time of its own schedule and not more, however slow its turns are: how fast the thread pool is (a cold process
    /// needs milliseconds for a first turn that a warm one does in microseconds) changes how long the test takes, not what it sees. A tick the manager has not yet taken waits in its timer (at most one), and a short-lived timer of
    /// the manager (the timeout of its last flush) can make a move that wakes nothing.
    /// </summary>
    public async Task RunClockUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100_000; i++)
        {
            Ct.ThrowIfCancellationRequested();
            if (await NextWakeUpAsync(condition) is not var (due, seen))
            {
                return;
            }

            var fired = Clock.TimersFired;
            Clock.MoveTo(due);

            // The provider may leave a callback to the thread that was reading the clock when it moved: until those have run it is not known what the move woke up.
            await UntilAsync(() => !Clock.HasTimerDueBy(due), "the clock did not run the timers that were due");
            if (Clock.TimersFired != fired)
            {
                await UntilAsync(() => condition() || Activity != seen, "the manager did not act on the move of the clock");
            }
        }

        condition().ShouldBeTrue("the condition was not reached while the clock ran");
    }

    /// <summary>
    /// Waits until the manager waits on a timer and says when it is due, together with what the manager had done up to then; null when the condition holds first. The two are read so that nothing happened in between: whatever the
    /// manager does after that is its answer to the move (a manager that finished its work between the look at the timer and the look at its activity would otherwise answer a move that has not been made yet). The instant is taken
    /// once: a timer can be gone a moment later (the manager disposes it), so it must not be asked for again.
    /// </summary>
    private async Task<(DateTimeOffset Due, long Seen)?> NextWakeUpAsync(Func<bool> condition)
    {
        (DateTimeOffset Due, long Seen)? wake = null;
        await PollAsync(
            () =>
            {
                if (condition())
                {
                    return true;
                }

                var seen = Activity;
                if (Clock.NextTimerDue is { } due && Activity == seen)
                {
                    wake = (due, seen);
                    return true;
                }

                return false;
            },
            "the manager waits for no timer, so there is no time to move",
            ThrowIfTheSessionFailed);
        return wake;
    }

    /// <summary>A session that ended with an exception would otherwise show as 'waits for no timer' 30 seconds later.</summary>
    private void ThrowIfTheSessionFailed()
    {
        if (_run is { IsFaulted: true } run)
        {
            ExceptionDispatchInfo.Capture(run.Exception!.GetBaseException()).Throw();
        }
    }

    /// <summary>Starts the session and waits until it is subscribed and streaming (state connected or idle).</summary>
    public async Task StartConnectedAsync()
    {
        Begin();
        await UntilAsync(() => Manager.State is FeedStates.Connected or FeedStates.Idle && Changes.Watching);
    }

    public void Dispose() => _stop.Dispose();
}
