using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Recording;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// The expired-options backfill's own run owner (P14-03 criteria 5, 6, 7, 9), beside the live feed's and the other recorders' supervisors and never in their way: its own try/catch, its own run record (<c>options-backfill</c>), its own heartbeat row
/// (<c>xd-backfill</c>). Every <see cref="Interval"/> (15 s) it decides whether the backfill should be up. <b>All four gates must be open</b>: the deployment flag <c>Options:Backfill:Enabled</c>, the owner's stored switch (newest row ON; no row = OFF), the
/// owner's acceptance of the probe result (no row = not accepted) and the service <c>options-backfill</c> (enabled). It also runs only inside the night window 16:30 to 08:30 India time. With any gate closed, or one that cannot be read, nothing reads the
/// credentials, builds a request or sends. Before each start the disk guard is asked (own cap <c>Options:Backfill:MaxStoredGb</c>, the free floor <c>Options:MinFreeGb</c>); the job asks again before every window. A refused token waits for a new one;
/// "not subscribed" ends the day; the daily cap waits for the next India day. The app never deletes data.
/// </summary>
internal sealed class OptionBackfillSupervisor(IServiceScopeFactory scopes, IBackfillSession session, OptionBackfillSettings settings, IApplicationInfo application, TimeProvider clock, ILogger<OptionBackfillSupervisor> logger) : IOptionBackfillSupervisor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(20);

    public static readonly TimeSpan TokenCheckInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan CrashPause = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan ProgressEvery = TimeSpan.FromMinutes(5);

    public const string TokenRefusedMessage = "Token refused: Dhan refused the Data account's token for the expired-options backfill; waiting for a new token.";

    public const string NotSubscribedMessage = "Not subscribed: Dhan answered that the Data APIs are not part of the plan (DH-902); the backfill ended for the day.";

    public const string NoTokenLostMessage = "No usable token: the Data account is not connected any more.";

    public const string DiskBudgetMessage = "Disk budget reached: the stored backfill is over its cap. Fetching stopped; nothing was deleted.";

    public const string DiskFreeMessage = "Low free disk space: fetching stopped; nothing was deleted.";

    public const string CrashMessage = "The expired-options backfill stopped unexpectedly. See the xd-feed log.";

    public const string InterruptedMessage = "Interrupted: the feed host restarted while the backfill was running.";

    public const string NoTokenMessage = "No usable token";

    public const string NoUnderlyingsMessage = "No NIFTY, BANKNIFTY or share targets in the option underlyings";

    public const string StoreFailedMessage = "Store failed: three answers in a row could not be stored; the backfill waits 5 minutes. Nothing was deleted.";

    public const string RateLimitedMessage = "Rate limited: five answers in a row were rate limited by Dhan; the backfill waits 15 minutes.";

    public const string TooManyGivenUpMessage = "Too many keys given up: five keys in a row failed three times; the backfill ended for the day. See the fetch log.";

    public const string BadRequestMessage = "Bad request: Dhan answered DH-905 (the request itself is wrong); the backfill ended for the day. See the probe and the fetch log.";

    public static readonly TimeSpan StoreFailPause = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan RatePause = TimeSpan.FromMinutes(15);

    public const string DoneMessage = "Every planned key is finished or given up.";

    public const string DailyCapMessage = "The day's request cap was reached; the backfill goes on the next India day.";

    private Running? _running;
    private DateTimeOffset? _startedAt;
    private bool _recovered;
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextTokenCheck = DateTimeOffset.MinValue;
    private bool _tokenRefused;
    private string? _refusedFingerprint;
    private string? _waiting;
    private DateOnly? _skippedOn;
    private DateOnly? _endedOn;
    private DateOnly? _endedNight;
    private bool _diskLatched;
    private BackfillProgress? _progress;
    private DateTimeOffset _progressAt = DateTimeOffset.MinValue;
    private BackfillRunStats _last = new(0, 0, 0, 0, 0, 0, 0);

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

    /// <summary>One look at the gates, the run and the heartbeat. Internal to the tests, which drive the clock. The heartbeat goes out even when the look fails; a failed look is logged and the next one tries again.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = _waiting ?? RecorderStates.Closed;
        try
        {
            _startedAt ??= now;
            state = await LookAsync(now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed look must not end the backfill; the next one is in a few seconds
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionBackfillLog.SupervisorFailed(logger, ex);
        }

        await ReportAsync(state, cancellationToken);
    }

    private async Task<string> LookAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            // Gate 1: nothing below this line runs - no credentials, no request, no store.
            return RecorderStates.Disabled;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var runs = services.GetRequiredService<IServiceRunStore>();
        if (!_recovered)
        {
            await RecoverAsync(runs, now, cancellationToken);
            _recovered = true;
        }

        bool switchOn;
        bool acknowledged;
        ServiceSettings service;
        try
        {
            switchOn = (await services.GetRequiredService<IOptionBackfillSwitchStore>().CurrentAsync(cancellationToken))?.Enabled == true;
            acknowledged = await services.GetRequiredService<IBackfillProbeAckStore>().CurrentAsync(cancellationToken) is not null;
            service = (await services.GetRequiredService<ServiceSettingsReader>().ReadAsync(OptionBackfillService.Definition, cancellationToken)).Settings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // A gate that cannot be read is a closed gate: a running session is stopped, nothing new is started. Only the type of the exception is logged.
            OptionBackfillLog.GateReadFailed(logger, ex.GetType().Name);
            if (_running is { Task.IsCompleted: true })
            {
                await EndRunAsync(runs, now, cancellationToken);
            }

            if (_running is not null)
            {
                await StopRunAsync(runs, OptionBackfillService.Paused, cancellationToken);
            }

            return RecorderStates.Paused;
        }

        await RefreshProgressAsync(services, now, cancellationToken);
        var windowOpen = BackfillWindow.IsOpen(now);
        var shouldBeUp = switchOn && service.Enabled && acknowledged && windowOpen;

        if (_running is { Task.IsCompleted: true })
        {
            await EndRunAsync(runs, now, cancellationToken);
        }

        if (_running is not null && !shouldBeUp)
        {
            await StopRunAsync(runs, !switchOn || !service.Enabled || !acknowledged ? OptionBackfillService.Paused : OptionBackfillService.WindowEnded, cancellationToken);
        }

        if (_running is null && shouldBeUp)
        {
            await TryOpenRunAsync(services, runs, now, cancellationToken);
        }
        else if (_running is null)
        {
            _waiting = null;
            _tokenRefused = false;
            _refusedFingerprint = null;
            _diskLatched = false; // a stop for the disk holds until a look finds the backfill not wanted (the owner's pause, the end of the night) and the guard agrees at the next start
            _resumeAt = DateTimeOffset.MinValue;
        }

        if (_running is not null)
        {
            return session.State;
        }

        if (shouldBeUp)
        {
            return _waiting ?? RecorderStates.Connecting;
        }

        return !switchOn ? RecorderStates.SwitchOff : !service.Enabled ? RecorderStates.Paused : !acknowledged ? BackfillStates.ProbeNotAccepted : BackfillStates.OutsideWindow;
    }

    /// <summary>The first look of a process: the run the previous process left open is closed as failed (interrupted).</summary>
    private async Task RecoverAsync(IServiceRunStore runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await runs.GetLatestAsync([OptionBackfillService.Id], cancellationToken);
        if (latest.TryGetValue(OptionBackfillService.Id, out var run) && run.IsRunning && run.StartedAt < _startedAt)
        {
            await runs.CompleteAsync(run.Id, ServiceRunStatus.Failed, InterruptedMessage, now, cancellationToken);
        }
    }

    private async Task TryOpenRunAsync(IServiceProvider services, IServiceRunStore runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(now);
        if (_endedOn == today || _endedNight == BackfillWindow.NightOf(now) || _diskLatched || now < _resumeAt || (_tokenRefused && now < _nextTokenCheck))
        {
            return;
        }

        // Before each start: the disk guard (a stopped backfill stays stopped while the condition holds).
        var store = services.GetRequiredService<IRollingBarStore>();
        var stored = await store.StoredBytesAsync(cancellationToken);
        var free = await store.FreeBytesAsync(cancellationToken);
        var verdict = RecorderDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes);
        if (verdict is RecorderDiskVerdict.OverBudget or RecorderDiskVerdict.LowFree)
        {
            var over = verdict == RecorderDiskVerdict.OverBudget;
            _diskLatched = true;
            _waiting = over ? RecorderStates.DiskBudget : RecorderStates.DiskFree;
            await SkipOncePerDayAsync(runs, today, over ? DiskBudgetMessage : DiskFreeMessage, now, cancellationToken);
            return;
        }

        var targetSource = services.GetRequiredService<IBackfillTargets>();
        await targetSource.FreezeStocksAsync(cancellationToken); // all gates are open: the ranked shares are fixed once, so the plan does not move with the ranking
        var targets = await targetSource.LoadAsync(cancellationToken);
        var indices = targets.Count(t => t.IsIndex);
        OptionBackfillLog.Targets(logger, indices, targets.Count - indices);
        var planned = BackfillPlanner.Plan(targets, settings, today).Count();
        if (planned == 0)
        {
            _waiting = RecorderStates.NoInstruments;
            await SkipOncePerDayAsync(runs, today, NoUnderlyingsMessage, now, cancellationToken);
            return;
        }

        var credentials = await services.GetRequiredService<IDataAccountCredentials>().GetCredentialsAsync(cancellationToken);
        if (_tokenRefused)
        {
            _nextTokenCheck = now + TokenCheckInterval;
        }

        if (credentials is null)
        {
            _waiting = _tokenRefused ? RecorderStates.TokenRefused : RecorderStates.NoToken;
            if (!_tokenRefused)
            {
                await SkipOncePerDayAsync(runs, today, NoTokenMessage, now, cancellationToken);
            }

            return;
        }

        var fingerprint = FeedCredentialFingerprint.Of(credentials);
        if (_tokenRefused)
        {
            if (_refusedFingerprint == fingerprint)
            {
                _waiting = RecorderStates.TokenRefused;
                return;
            }

            _tokenRefused = false;
            _refusedFingerprint = null;
        }

        var run = ServiceRun.Start(OptionBackfillService.Id, ServiceRunTrigger.Scheduled, 1, now);
        if (!await runs.TryStartAsync(run, StaleAfter, cancellationToken))
        {
            OptionBackfillLog.RunNotOpened(logger);
            return;
        }

        var stop = new CancellationTokenSource();
        _running = new Running(run, stop, session.RunAsync(targets, stop.Token), now, fingerprint);
        _waiting = null;
        OptionBackfillLog.RunOpened(logger, run.Id, planned);
    }

    private async Task SkipOncePerDayAsync(IServiceRunStore runs, DateOnly today, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_skippedOn == today)
        {
            return;
        }

        _skippedOn = today;
        await runs.RecordAsync(ServiceRun.Skipped(OptionBackfillService.Id, ServiceRunTrigger.Scheduled, message, now), cancellationToken);
    }

    /// <summary>The session ended by itself: close the run with what happened, remember the end for the day and set up the wait that follows.</summary>
    private async Task EndRunAsync(IServiceRunStore runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = _running!;
        BackfillSessionResult? result = null;
        try
        {
            result = await running.Task;
        }
#pragma warning disable CA1031 // a crashed session fails its run; the details are in the log
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionBackfillLog.SessionCrashed(logger, ex);
        }

        var today = IndiaTime.Today(now);
        ServiceRunStatus status;
        string message;
        switch (result?.End)
        {
            case BackfillSessionEnd.Completed:
                (status, message) = (ServiceRunStatus.Succeeded, DoneMessage);
                _endedNight = BackfillWindow.NightOf(running.StartedAt);
                _waiting = BackfillStates.Done;
                break;
            case BackfillSessionEnd.TokenRefused:
                (status, message) = (ServiceRunStatus.Failed, TokenRefusedMessage);
                _tokenRefused = true;
                _refusedFingerprint = running.Fingerprint;
                _nextTokenCheck = now + TokenCheckInterval;
                _waiting = RecorderStates.TokenRefused;
                break;
            case BackfillSessionEnd.NotSubscribed:
                (status, message) = (ServiceRunStatus.Failed, NotSubscribedMessage);
                _endedNight = BackfillWindow.NightOf(running.StartedAt);
                _waiting = RecorderStates.NotSubscribed;
                break;
            case BackfillSessionEnd.NoToken:
                (status, message) = (ServiceRunStatus.Failed, NoTokenLostMessage);
                _waiting = RecorderStates.NoToken;
                break;
            case BackfillSessionEnd.DiskBudget:
                (status, message) = (ServiceRunStatus.Failed, DiskBudgetMessage);
                _diskLatched = true;
                _waiting = RecorderStates.DiskBudget;
                break;
            case BackfillSessionEnd.DiskFree:
                (status, message) = (ServiceRunStatus.Failed, DiskFreeMessage);
                _diskLatched = true;
                _waiting = RecorderStates.DiskFree;
                break;
            case BackfillSessionEnd.StoreFailed:
                (status, message) = (ServiceRunStatus.Failed, StoreFailedMessage);
                _resumeAt = now + StoreFailPause;
                _waiting = BackfillStates.StoreFailed;
                break;
            case BackfillSessionEnd.RateLimited:
                (status, message) = (ServiceRunStatus.Failed, RateLimitedMessage);
                _resumeAt = now + RatePause;
                _waiting = BackfillStates.RatePaused;
                break;
            case BackfillSessionEnd.TooManyGivenUp:
                (status, message) = (ServiceRunStatus.Failed, TooManyGivenUpMessage);
                _endedNight = BackfillWindow.NightOf(running.StartedAt);
                _waiting = BackfillStates.TooManyFailures;
                break;
            case BackfillSessionEnd.BadRequest:
                (status, message) = (ServiceRunStatus.Failed, BadRequestMessage);
                _endedNight = BackfillWindow.NightOf(running.StartedAt);
                _waiting = BackfillStates.BadRequest;
                break;
            case BackfillSessionEnd.DailyCap:
                (status, message) = (ServiceRunStatus.Succeeded, DailyCapMessage);
                _endedOn = today;
                _waiting = BackfillStates.DailyCap;
                break;
            case BackfillSessionEnd.WindowEnd or BackfillSessionEnd.Stopped:
                (status, message) = (ServiceRunStatus.Succeeded, running.StopReason ?? OptionBackfillService.WindowEnded);
                break;
            default:
                (status, message) = (ServiceRunStatus.Failed, CrashMessage);
                _resumeAt = now + CrashPause;
                _waiting = RecorderStates.Connecting;
                break;
        }

        _last = result?.Stats ?? session.Stats;
        _progressAt = DateTimeOffset.MinValue; // refresh the figures at the next look
        await CloseRunAsync(runs, running.Run, status, message, now, cancellationToken);
        running.Stop.Dispose();
        _running = null;
    }

    /// <summary>A gate closed, the window ended or the process is stopping: tell the session to stop and wait for it. When it has not ended in time the run stays open and the next look waits again.</summary>
    private async Task StopRunAsync(IServiceRunStore runs, string reason, CancellationToken cancellationToken)
    {
        var running = _running!;
        running.StopReason ??= reason;
        await running.Stop.CancelAsync();
        try
        {
            await running.Task.WaitAsync(StopWait, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            // not ended in time
        }
#pragma warning disable CA1031 // the session reports its own failures; here only the end matters
        catch (Exception)
#pragma warning restore CA1031
        {
            // see above
        }

        if (!running.Task.IsCompleted)
        {
            OptionBackfillLog.StopTimedOut(logger, running.Run.Id);
            return;
        }

        await EndRunAsync(runs, clock.GetUtcNow(), cancellationToken);
        _waiting = null;
    }

    /// <summary>The progress figures, from the plan and the fetch log, when the deployment flag is on: at most every five minutes and after a run ends. Reads no bar and no credentials.</summary>
    private async Task RefreshProgressAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now - _progressAt < ProgressEvery)
        {
            return;
        }

        try
        {
            _progress = await services.GetRequiredService<BackfillProgressReader>().ReadAsync(cancellationToken);
            _progressAt = now;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            _progressAt = now;
            OptionBackfillLog.ProgressFailed(logger, ex.GetType().Name);
        }
    }

    private async Task CloseRunAsync(IServiceRunStore runs, ServiceRun run, ServiceRunStatus status, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await runs.CompleteAsync(run.Id, status, ServiceRun.Limit(message), now, cancellationToken);
            var code = status.ToCode();
            OptionBackfillLog.RunClosed(logger, run.Id, code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a run record that cannot be closed now is closed as interrupted at the next start
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionBackfillLog.RunNotClosed(logger, run.Id, ex);
        }
    }

    /// <summary>The heartbeat, from a scope of its own; one that cannot be written is logged and the loop goes on.</summary>
    private async Task ReportAsync(string state, CancellationToken cancellationToken)
    {
        try
        {
            var stats = _running is not null ? session.Stats : _last;
            var details = new BackfillHeartbeat(settings.Enabled, settings.RequestsPerSecond, stats.RequestsToday, stats.Ok + stats.Empty, stats.Gaps, stats.Bars, session.FreeSpaceReadable, _progress);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IOptionBackfillHeartbeats>().ReportAsync(state, details, application.Instance, application.Version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the next look reports again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionBackfillLog.SupervisorFailed(logger, ex);
        }
    }

    /// <summary>The process is stopping: end the session and close the run, then say in a last heartbeat that the stop is on purpose.</summary>
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
                    await EndRunAsync(runs, clock.GetUtcNow(), wait.Token);
                }
                else
                {
                    await StopRunAsync(runs, OptionBackfillService.Stopped, wait.Token);
                }
            }

            await scope.ServiceProvider.GetRequiredService<IOptionBackfillHeartbeats>().ReportAsync(RecorderStates.Stopped, new BackfillHeartbeat(settings.Enabled, settings.RequestsPerSecond, 0, 0, 0, 0, session.FreeSpaceReadable, _progress), application.Instance, application.Version, wait.Token);
        }
#pragma warning disable CA1031 // the process is going down; the record is closed as interrupted at the next start if this fails
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionBackfillLog.SupervisorFailed(logger, ex);
        }
    }

    private sealed class Running(ServiceRun run, CancellationTokenSource stop, Task<BackfillSessionResult> task, DateTimeOffset startedAt, string fingerprint)
    {
        public ServiceRun Run { get; } = run;

        public CancellationTokenSource Stop { get; } = stop;

        public Task<BackfillSessionResult> Task { get; } = task;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public string Fingerprint { get; } = fingerprint;

        public string? StopReason { get; set; }
    }
}
