using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Live;

/// <summary>
/// The feed's own window and run owner (ADR 0024 §1; the scheduler of <c>xd-api</c> cannot run a 6 h 35 min window and caps a run at 240 minutes, so <c>xd-feed</c> owns its run). Every <see cref="Interval"/> (15 s) it reads the
/// <c>live-feed</c> settings row (enabled, paused, the window; <see cref="ServiceSettingsReader"/>), the trading calendar and the manual-run marker <see cref="IFeedManualRun.Key"/> and decides, with <see cref="FeedWindow"/>, whether the
/// feed should be up:
/// <list type="bullet">
/// <item>it should and is not: the Data account's credentials are read - none → one <c>skipped - No usable token</c> run per India day and the state <c>no_token</c>; a token Dhan refused earlier → nothing until the token changes (the
/// credentials are looked at once a minute); otherwise <b>one</b> run record is opened (trigger <c>scheduled</c>, or <c>manual</c> while the marker is in force) and the <see cref="IFeedSession"/> starts;</item>
/// <item>it should not and is up: the session is cancelled (a clean stop with a last flush) and the run is closed as succeeded with the reason (<see cref="LiveFeedService.WindowEnded"/> or <see cref="LiveFeedService.Paused"/>);</item>
/// <item>the session ended by itself: token refused or flapping close the run as <b>failed</b> (the failed run is what raises the service failure notice of ADR 0018 §7 on the Services screen and the bell), flapping then pauses new
/// attempts for 5 minutes; a crash fails the run and waits 30 s.</item>
/// </list>
/// A run found still open at the first look is the previous process's (it crashed or was killed): it is closed as failed (interrupted). The <b>heartbeat</b> goes out at every look, in or out of the window, with the state word of
/// <see cref="FeedStates"/> and the figures of <see cref="FeedMetrics"/>. Failures of a look are logged and the next look tries again.
/// </summary>
internal sealed class LiveFeedSupervisor(IServiceScopeFactory scopes, IFeedSession session, FeedMetrics metrics, IApplicationInfo application, TimeProvider clock, ILogger<LiveFeedSupervisor> logger) : IFeedSupervisor
{
    /// <summary>How often the window is looked at and the heartbeat goes out.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>A run open for longer than this cannot be running (a window is under 8 hours): the store closes it as interrupted when a new run starts.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(8);

    /// <summary>How often the credentials are looked at while a refused token is waited out.</summary>
    public static readonly TimeSpan TokenCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>How long a session that crashed is left alone before it is started again.</summary>
    public static readonly TimeSpan CrashPause = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait for a session to stop.</summary>
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);

    public const string FlappingMessage = "Flapping: more than 10 reconnects in 5 minutes; attempts are paused for 5 minutes.";

    public const string TokenRefusedMessage = "Token refused: the Data account's token was refused by Dhan; waiting for a new token.";

    public const string NoTokenLostMessage = "No usable token: the Data account is not connected any more.";

    public const string CrashMessage = "The feed stopped unexpectedly. See the xd-feed log.";

    public const string InterruptedMessage = "Interrupted: the feed restarted while the window was open.";

    private Running? _running;
    private DateTimeOffset? _startedAt;
    private bool _recovered;
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextTokenCheck = DateTimeOffset.MinValue;
    private bool _tokenRefused;
    private string? _refusedFingerprint;
    private string? _waiting;
    private DateOnly? _skippedOn;
    private DateOnly? _clearedOn;
    private FeedWindowDecision? _lastDecision;

    /// <summary>The task of the session in progress; null when none is. For tests.</summary>
    internal Task? SessionTask => _running?.Task;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                await TickAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopping
        }
        finally
        {
            await ShutdownAsync();
        }
    }

    /// <summary>
    /// One look at the window, the run and the heartbeat. Public to the tests, which drive the clock. The heartbeat goes out even when the look fails (with the state of the last look that worked), so a failing database read
    /// does not make a feed that is streaming look dead; a failed look is logged and the next one tries again.
    /// </summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        try
        {
            _startedAt ??= now;
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var runs = services.GetRequiredService<IServiceRunStore>();
            if (!_recovered)
            {
                await CloseLeftoverAsync(runs, cancellationToken);
                _recovered = true;
            }

            var settings = (await services.GetRequiredService<ServiceSettingsReader>().ReadAsync(LiveFeedService.Definition, cancellationToken)).Settings;
            var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            var manualUntil = await services.GetRequiredService<IFeedManualRun>().GetManualUntilAsync(cancellationToken);
            var decision = FeedWindow.Decide(settings, calendar, now, manualUntil);
            _lastDecision = decision;

            if (_running is { Task.IsCompleted: true })
            {
                await EndRunAsync(runs, now, cancellationToken);
            }

            if (_running is not null && !decision.ShouldBeUp)
            {
                await StopRunAsync(runs, decision.DownState == FeedStates.Paused ? LiveFeedService.Paused : LiveFeedService.WindowEnded, cancellationToken);
            }

            if (_running is null && decision.ShouldBeUp)
            {
                await TryOpenRunAsync(services, runs, decision, now, cancellationToken);
            }
            else if (!decision.ShouldBeUp && _running is null)
            {
                // outside the window every wait is over: the next window starts with a fresh attempt
                _waiting = null;
                _tokenRefused = false;
                _refusedFingerprint = null;
                _resumeAt = DateTimeOffset.MinValue;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed look must not end the feed; the next one is in a few seconds
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.SupervisorFailed(logger, ex);
        }

        await ReportAsync(_lastDecision, now, cancellationToken);
    }

    /// <summary>The run the previous process left open is closed as failed (interrupted); the window is then opened afresh if it is still on.</summary>
    private async Task CloseLeftoverAsync(IServiceRunStore runs, CancellationToken cancellationToken)
    {
        var latest = await runs.GetLatestAsync([LiveFeedService.Id], cancellationToken);
        if (latest.TryGetValue(LiveFeedService.Id, out var run) && run.IsRunning && run.StartedAt < _startedAt)
        {
            await runs.CompleteAsync(run.Id, ServiceRunStatus.Failed, InterruptedMessage, clock.GetUtcNow(), cancellationToken);
            LiveFeedLog.InterruptedRunClosed(logger, run.Id);
        }
    }

    private async Task TryOpenRunAsync(IServiceProvider services, IServiceRunStore runs, FeedWindowDecision decision, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now < _resumeAt)
        {
            return; // flapping or a crash: attempts are paused; _waiting says which
        }

        if (_tokenRefused && now < _nextTokenCheck)
        {
            return;
        }

        var credentials = await services.GetRequiredService<IDataAccountCredentials>().GetCredentialsAsync(cancellationToken);
        if (_tokenRefused)
        {
            _nextTokenCheck = now + TokenCheckInterval;
        }

        if (credentials is null)
        {
            _waiting = _tokenRefused ? FeedStates.TokenRefused : FeedStates.NoToken;
            var today = XDrishti.Domain.Market.IndiaTime.Today(now);
            if (!_tokenRefused && _skippedOn != today)
            {
                _skippedOn = today;
                await runs.RecordAsync(ServiceRun.Skipped(LiveFeedService.Id, decision.Manual ? ServiceRunTrigger.Manual : ServiceRunTrigger.Scheduled, LiveFeedService.NoUsableToken, now), cancellationToken);
                LiveFeedLog.NoToken(logger);
            }

            return;
        }

        if (_tokenRefused)
        {
            if (_refusedFingerprint == FeedCredentialFingerprint.Of(credentials))
            {
                _waiting = FeedStates.TokenRefused;
                return;
            }

            _tokenRefused = false;
            _refusedFingerprint = null;
        }

        var run = ServiceRun.Start(LiveFeedService.Id, decision.Manual ? ServiceRunTrigger.Manual : ServiceRunTrigger.Scheduled, 1, now);
        if (!await runs.TryStartAsync(run, StaleAfter, cancellationToken))
        {
            LiveFeedLog.RunNotOpened(logger);
            return;
        }

        await ClearLiveQuotesOncePerDayAsync(services, now, cancellationToken);
        var stop = new CancellationTokenSource();
        var token = stop.Token;
        _running = new Running(run, stop, session.RunAsync(token)); // an async method: it returns at its first await and its failures end up in the task
        _waiting = null;
        var triggerCode = run.Trigger.ToCode();
        LiveFeedLog.RunOpened(logger, run.Id, triggerCode);
    }

    /// <summary>
    /// <c>market.live_quotes</c> is emptied once per window - the first run opened on an India day - not per session: a flapping connection, a refused token that comes back or a crashed session opens a run again the same day and must
    /// not wipe the index quotes the screen shows.
    /// </summary>
    private async Task ClearLiveQuotesOncePerDayAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = XDrishti.Domain.Market.IndiaTime.Today(now);
        if (_clearedOn == today)
        {
            return;
        }

        try
        {
            await services.GetRequiredService<ILiveQuotes>().ClearAsync(cancellationToken);
            _clearedOn = today;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // old quotes of an earlier window are harmless (the sink only takes newer ticks); the window must not wait for the clean-up
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _clearedOn = today;
            LiveFeedLog.ClearFailed(logger, ex);
        }
    }

    /// <summary>The session ended by itself: close the run with what happened and set up the wait that follows.</summary>
    private async Task EndRunAsync(IServiceRunStore runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = _running!;
        FeedSessionResult? result = null;
        try
        {
            result = await running.Task;
        }
#pragma warning disable CA1031 // a crashed session fails its run; the details are in the log
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.SessionCrashed(logger, ex);
        }

        ServiceRunStatus status;
        string message;
        switch (result?.End)
        {
            case FeedSessionEnd.TokenRefused:
                (status, message) = (ServiceRunStatus.Failed, TokenRefusedMessage);
                _tokenRefused = true;
                _refusedFingerprint = result.CredentialFingerprint;
                _nextTokenCheck = now + TokenCheckInterval;
                _waiting = FeedStates.TokenRefused;
                break;
            case FeedSessionEnd.Flapping:
                (status, message) = (ServiceRunStatus.Failed, FlappingMessage);
                _resumeAt = now + ReconnectLimiter.Pause;
                _waiting = FeedStates.Flapping;
                break;
            case FeedSessionEnd.NoToken:
                (status, message) = (ServiceRunStatus.Failed, NoTokenLostMessage);
                _waiting = FeedStates.NoToken;
                break;
            case FeedSessionEnd.Stopped:
                (status, message) = (ServiceRunStatus.Succeeded, running.StopReason ?? LiveFeedService.Stopped);
                break;
            default:
                (status, message) = (ServiceRunStatus.Failed, CrashMessage);
                _resumeAt = now + CrashPause;
                _waiting = FeedStates.Connecting;
                break;
        }

        await CloseRunAsync(runs, running.Run, status, message, now, cancellationToken);
        running.Stop.Dispose();
        _running = null;
    }

    /// <summary>
    /// The window ended, the service was paused or the process is stopping: tell the session to stop and wait for it. When it has ended the run is closed as succeeded with the reason. When it has not ended in time the run stays
    /// open and <c>_running</c> stays set - no second session is opened over a connection that is still up - and the next look waits again; the reason is remembered for when it ends.
    /// </summary>
    private async Task StopRunAsync(IServiceRunStore runs, string reason, CancellationToken cancellationToken)
    {
        var running = _running!;
        running.StopReason ??= reason;
        await running.Stop.CancelAsync();
        await WaitForAsync(running.Task);
        if (!running.Task.IsCompleted)
        {
            LiveFeedLog.StopTimedOut(logger, running.Run.Id);
            return;
        }

        await EndRunAsync(runs, clock.GetUtcNow(), cancellationToken);
        _waiting = null;
    }

    private async Task CloseRunAsync(IServiceRunStore runs, ServiceRun run, ServiceRunStatus status, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await runs.CompleteAsync(run.Id, status, ServiceRun.Limit(message), now, cancellationToken);
            var statusCode = status.ToCode();
            LiveFeedLog.RunClosed(logger, run.Id, statusCode, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a run record that cannot be closed now is closed as interrupted at the next start; the feed goes on
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.RunNotClosed(logger, run.Id, ex);
        }
    }

    private async Task WaitForAsync(Task task)
    {
        try
        {
            await task.WaitAsync(StopWait, clock);
        }
        catch (TimeoutException)
        {
            // the session did not stop in time; the run is closed anyway
        }
#pragma warning disable CA1031 // the session reports its own failures; here only the end matters
        catch (Exception)
#pragma warning restore CA1031
        {
            // see above
        }
    }

    /// <summary>The heartbeat, from a scope of its own; a heartbeat that cannot be written is logged and the loop goes on.</summary>
    private async Task ReportAsync(FeedWindowDecision? decision, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = _running is not null
            ? session.State
            : decision is { ShouldBeUp: true } ? _waiting ?? FeedStates.Connecting : decision?.DownState ?? FeedStates.Closed;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IFeedHeartbeats>().ReportAsync(state, metrics.Snapshot(now), application.Instance, application.Version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the next look reports again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.SupervisorFailed(logger, ex);
        }
    }

    /// <summary>
    /// The process is stopping: end the session and close the run with <see cref="LiveFeedService.Stopped"/> (nobody cancels this part), then say in a last heartbeat that the stop is on purpose
    /// (<see cref="FeedStates.Stopped"/>), so the System status page does not take the stopped container for a crash.
    /// </summary>
    private async Task ShutdownAsync()
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            using var wait = new CancellationTokenSource(StopWait, clock);
            var runs = scope.ServiceProvider.GetRequiredService<IServiceRunStore>();
            if (_running is { } running)
            {
                if (running.Task.IsCompleted)
                {
                    await EndRunAsync(runs, clock.GetUtcNow(), wait.Token); // the session had ended by itself (a refused token, flapping): that is what the record says, not "stopped"
                }
                else
                {
                    await StopRunAsync(runs, LiveFeedService.Stopped, wait.Token);
                }
            }

            await scope.ServiceProvider.GetRequiredService<IFeedHeartbeats>().ReportAsync(FeedStates.Stopped, metrics.Snapshot(clock.GetUtcNow()), application.Instance, application.Version, wait.Token);
        }
#pragma warning disable CA1031 // the process is going down; the record is closed as interrupted at the next start if this fails
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LiveFeedLog.SupervisorFailed(logger, ex);
        }
    }

    private sealed class Running(ServiceRun run, CancellationTokenSource stop, Task<FeedSessionResult> task)
    {
        public ServiceRun Run { get; } = run;

        public CancellationTokenSource Stop { get; } = stop;

        public Task<FeedSessionResult> Task { get; } = task;

        /// <summary>Why the supervisor asked the session to stop (window end, paused, process stopping); null while it was not asked.</summary>
        public string? StopReason { get; set; }
    }
}
