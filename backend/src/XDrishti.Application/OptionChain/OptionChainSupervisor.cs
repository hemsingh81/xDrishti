using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Recording;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.OptionChain;

/// <summary>
/// The option-chain recorder's own run owner (P14-02 criteria 6, 7, 8), beside the live feed's and the other recorders' supervisors and never in their way: its own try/catch, its own run record (<c>option-chain-recorder</c>), its own heartbeat row
/// (<c>xd-options</c>). Every <see cref="Interval"/> (15 s) it decides whether recording should be up. <b>All three gates must be open</b>: the deployment flag <c>Options:Chain:Enabled</c>, the owner's stored switch (newest row ON; no row = OFF) and the
/// service <c>option-chain-recorder</c> (enabled, inside its window on a trading day). With any gate closed, or one that cannot be read, nothing reads the credentials, builds a request or sends. Before each start the disk guard is asked (the recorder's
/// own cap <c>Options:Chain:MaxStoredGb</c>, the free floor <c>Options:MinFreeGb</c>). The <c>options.chain_sessions</c> row is written when the day is over; a day with request rows and no end row is closed as <c>crashed</c> at the next start.
/// A rate-limit pause is 15 minutes; a refused token waits for a new one; "not subscribed" ends the day. The app never deletes data.
/// </summary>
internal sealed class OptionChainSupervisor(IServiceScopeFactory scopes, IChainSession session, OptionChainSettings settings, IApplicationInfo application, TimeProvider clock, ILogger<OptionChainSupervisor> logger) : IOptionChainSupervisor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(8);

    public static readonly TimeSpan TokenCheckInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan CrashPause = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);

    public const string RatePausedMessage = "Rate limited: Dhan's rate limit was hit in three slots in a row; requests are paused for 15 minutes.";

    public const string TokenRefusedMessage = "Token refused: Dhan refused the Data account's token for the option chain; waiting for a new token.";

    public const string NotSubscribedMessage = "Not subscribed: Dhan answered that the Data APIs are not part of the plan (DH-902); recording ended for the day.";

    public const string NoTokenLostMessage = "No usable token: the Data account is not connected any more.";

    public const string DiskBudgetMessage = "Disk budget reached: the stored option chains are over their cap. Recording stopped; nothing was deleted.";

    public const string DiskFreeMessage = "Low free disk space: recording stopped; nothing was deleted.";

    public const string CrashMessage = "The option-chain recorder stopped unexpectedly. See the xd-feed log.";

    public const string InterruptedMessage = "Interrupted: the feed host restarted while the window was open.";

    public const string NoTokenMessage = "No usable token";

    public const string NoUnderlyingsMessage = "No option underlyings to record";

    public const string DayDoneMessage = "Every slot of the day was recorded.";

    public const string CrashedNote = "The process ended before the day was closed.";

    public const string RateLimitedReason = "rate_limited";

    private readonly TimeOnly _dayOver = OptionChainRecorderService.WindowEnd;
    private Running? _running;
    private Day? _day;
    private DateTimeOffset? _startedAt;
    private bool _recovered;
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextTokenCheck = DateTimeOffset.MinValue;
    private bool _tokenRefused;
    private string? _refusedFingerprint;
    private string? _waiting;
    private DateOnly? _skippedOn;
    private DateOnly? _endedOn;
    private bool _diskLatched;

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
#pragma warning disable CA1031 // a failed look must not end the recorder; the next one is in a few seconds
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionChainLog.SupervisorFailed(logger, ex);
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
        var store = services.GetRequiredService<IOptionChainStore>();
        if (!_recovered)
        {
            await RecoverAsync(runs, store, now, cancellationToken);
            _recovered = true;
        }

        bool switchOn;
        ServiceSettings service;
        TradingCalendar calendar;
        try
        {
            switchOn = (await services.GetRequiredService<IOptionChainSwitchStore>().CurrentAsync(cancellationToken))?.Enabled == true;
            service = (await services.GetRequiredService<ServiceSettingsReader>().ReadAsync(OptionChainRecorderService.Definition, cancellationToken)).Settings;
            calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // A gate that cannot be read is a closed gate: a running session is stopped, nothing new is started. Only the type of the exception is logged.
            OptionChainLog.GateReadFailed(logger, ex.GetType().Name);
            if (_running is { Task.IsCompleted: true })
            {
                await EndRunAsync(runs, now, cancellationToken);
            }

            if (_running is not null)
            {
                await StopRunAsync(runs, OptionChainRecorderService.Paused, RecorderEndReasons.Paused, cancellationToken);
            }

            return RecorderStates.Paused;
        }

        var decision = FeedWindow.Decide(service, calendar, now, null);
        var shouldBeUp = switchOn && decision.ShouldBeUp;

        if (_running is { Task.IsCompleted: true })
        {
            await EndRunAsync(runs, now, cancellationToken);
        }

        if (_running is not null && !shouldBeUp)
        {
            await StopRunAsync(runs, !switchOn || decision.DownState == FeedStates.Paused ? OptionChainRecorderService.Paused : OptionChainRecorderService.WindowEnded, RecorderEndReasons.Paused, cancellationToken);
        }

        if (_running is null && shouldBeUp)
        {
            await TryOpenRunAsync(services, runs, store, now, cancellationToken);
        }
        else if (_running is null)
        {
            _waiting = null;
            _tokenRefused = false;
            _refusedFingerprint = null;
            _diskLatched = false; // a stop for the disk holds until a look finds the recorder not wanted (the owner's pause, the end of the window) and the guard agrees at the next start
            _resumeAt = DateTimeOffset.MinValue;
            await FinishDayIfOverAsync(store, now, cancellationToken);
        }

        if (_running is not null)
        {
            return session.State;
        }

        if (shouldBeUp)
        {
            return _waiting ?? RecorderStates.Connecting;
        }

        return !switchOn ? RecorderStates.SwitchOff : decision.DownState == FeedStates.Paused ? RecorderStates.Paused : RecorderStates.Closed;
    }

    /// <summary>The first look of a process: the run the previous process left open is closed as failed (interrupted); a day with request rows and no end row is closed as crashed.</summary>
    private async Task RecoverAsync(IServiceRunStore runs, IOptionChainStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await runs.GetLatestAsync([OptionChainRecorderService.Id], cancellationToken);
        if (latest.TryGetValue(OptionChainRecorderService.Id, out var run) && run.IsRunning && run.StartedAt < _startedAt)
        {
            await runs.CompleteAsync(run.Id, ServiceRunStatus.Failed, InterruptedMessage, now, cancellationToken);
        }

        // Today's date is skipped: a restart in the middle of the day must not take the day's real end row away (it is written once, the first wins). A day that is never closed is found tomorrow.
        var today = IndiaTime.Today(now);
        var days = (await store.FindUnfinishedDaysAsync(cancellationToken)).Where(d => d < today).ToList();
        foreach (var date in days)
        {
            var stored = await SafeStoredBytesAsync(store, cancellationToken);
            await store.RecordSessionAsync(new ChainSessionRecord(date, now, now, RecorderEndReasons.Crashed, 0, 0, 0, 0, 0, stored, CrashedNote), cancellationToken);
        }

        if (days.Count > 0)
        {
            OptionChainLog.CrashedDaysClosed(logger, days.Count);
        }
    }

    private async Task TryOpenRunAsync(IServiceProvider services, IServiceRunStore runs, IOptionChainStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(now);
        if (_endedOn == today || _diskLatched || now < _resumeAt || (_tokenRefused && now < _nextTokenCheck))
        {
            return;
        }

        // Before each start: the disk guard (a stopped recorder stays stopped while the condition holds).
        var stored = await store.StoredBytesAsync(cancellationToken);
        var free = await store.FreeBytesAsync(cancellationToken);
        var verdict = RecorderDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes);
        if (verdict is RecorderDiskVerdict.OverBudget or RecorderDiskVerdict.LowFree)
        {
            var over = verdict == RecorderDiskVerdict.OverBudget;
            _diskLatched = true;
            _waiting = over ? RecorderStates.DiskBudget : RecorderStates.DiskFree;
            await SkipOncePerDayAsync(runs, today, over ? DiskBudgetMessage : DiskFreeMessage, now, cancellationToken);
            DayOf(today, now).Last = over ? RecorderEndReasons.DiskBudget : RecorderEndReasons.DiskFree;
            return;
        }

        var day = await BuildDayAsync(services, today, cancellationToken);
        if (day.IsEmpty)
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

        var run = ServiceRun.Start(OptionChainRecorderService.Id, ServiceRunTrigger.Scheduled, 1, now);
        if (!await runs.TryStartAsync(run, StaleAfter, cancellationToken))
        {
            OptionChainLog.RunNotOpened(logger);
            return;
        }

        DayOf(today, now);
        var stop = new CancellationTokenSource();
        _running = new Running(run, stop, session.RunAsync(day, stop.Token), now, fingerprint);
        _waiting = null;
        OptionChainLog.RunOpened(logger, run.Id);
    }

    /// <summary>The day's underlyings: the indices that have an expiry rule, and every other listed underlying as a stock. The ids come from the master list through the app setting <c>options.underlyings</c>.</summary>
    private async Task<ChainDay> BuildDayAsync(IServiceProvider services, DateOnly today, CancellationToken cancellationToken)
    {
        var all = await services.GetRequiredService<IChainTargets>().LoadAsync(cancellationToken);
        var ruled = settings.IndexExpiries.Select(r => r.Symbol).ToHashSet(StringComparer.Ordinal);
        var day = new ChainDay(today, [.. all.Where(t => t.IsIndex && ruled.Contains(t.Symbol))], [.. all.Where(t => !t.IsIndex)]);
        OptionChainLog.Targets(logger, day.Indices.Count, day.Stocks.Count);
        return day;
    }

    private async Task SkipOncePerDayAsync(IServiceRunStore runs, DateOnly today, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_skippedOn == today)
        {
            return;
        }

        _skippedOn = today;
        await runs.RecordAsync(ServiceRun.Skipped(OptionChainRecorderService.Id, ServiceRunTrigger.Scheduled, message, now), cancellationToken);
    }

    /// <summary>The session ended by itself: close the run with what happened, remember the end for the day and set up the wait that follows.</summary>
    private async Task EndRunAsync(IServiceRunStore runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = _running!;
        ChainSessionResult? result = null;
        try
        {
            result = await running.Task;
        }
#pragma warning disable CA1031 // a crashed session fails its run; the details are in the log
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionChainLog.SessionCrashed(logger, ex);
        }

        var today = IndiaTime.Today(running.StartedAt);
        ServiceRunStatus status;
        string message;
        string reason;
        switch (result?.End)
        {
            case ChainSessionEnd.Completed:
                (status, message, reason) = (ServiceRunStatus.Succeeded, DayDoneMessage, RecorderEndReasons.WindowEnd);
                _endedOn = today;
                _waiting = ChainStates.DayDone;
                break;
            case ChainSessionEnd.TokenRefused:
                (status, message, reason) = (ServiceRunStatus.Failed, TokenRefusedMessage, RecorderEndReasons.TokenRefused);
                _tokenRefused = true;
                _refusedFingerprint = running.Fingerprint;
                _nextTokenCheck = now + TokenCheckInterval;
                _waiting = RecorderStates.TokenRefused;
                break;
            case ChainSessionEnd.NotSubscribed:
                (status, message, reason) = (ServiceRunStatus.Failed, NotSubscribedMessage, RecorderEndReasons.NotSubscribed);
                _endedOn = today;
                _waiting = RecorderStates.NotSubscribed;
                break;
            case ChainSessionEnd.RatePaused:
                // Shortcut: the 15-minute pause lives in memory (_resumeAt). A restart of xd-feed inside it forgets the pause; the 3-second throttle still holds, and a repeated limit pauses again after three slots (ADR 0176).
                (status, message, reason) = (ServiceRunStatus.Failed, RatePausedMessage, RateLimitedReason);
                _resumeAt = now + OptionChainRecorder.RatePause;
                _waiting = ChainStates.RatePaused;
                break;
            case ChainSessionEnd.NoToken:
                (status, message, reason) = (ServiceRunStatus.Failed, NoTokenLostMessage, RecorderEndReasons.TokenRefused);
                _waiting = RecorderStates.NoToken;
                break;
            case ChainSessionEnd.DiskBudget:
                (status, message, reason) = (ServiceRunStatus.Failed, DiskBudgetMessage, RecorderEndReasons.DiskBudget);
                _diskLatched = true;
                _waiting = RecorderStates.DiskBudget;
                break;
            case ChainSessionEnd.DiskFree:
                (status, message, reason) = (ServiceRunStatus.Failed, DiskFreeMessage, RecorderEndReasons.DiskFree);
                _diskLatched = true;
                _waiting = RecorderStates.DiskFree;
                break;
            case ChainSessionEnd.Stopped:
                (status, message, reason) = (ServiceRunStatus.Succeeded, running.StopReason ?? OptionChainRecorderService.Stopped, running.EndReason ?? RecorderEndReasons.WindowEnd);
                break;
            default:
                (status, message, reason) = (ServiceRunStatus.Failed, CrashMessage, RecorderEndReasons.Crashed);
                _resumeAt = now + CrashPause;
                _waiting = RecorderStates.Connecting;
                break;
        }

        var stats = result?.Stats ?? session.Stats;
        var day = DayOf(today, running.StartedAt);
        day.Slots += stats.Slots;
        day.Requests += stats.Requests;
        day.Ok += stats.Ok;
        day.Gaps += stats.Gaps;
        day.Rows += stats.Rows;
        day.Last = reason;
        await CloseRunAsync(runs, running.Run, status, message, now, cancellationToken);
        running.Stop.Dispose();
        _running = null;
    }

    /// <summary>A gate closed, the window ended or the process is stopping: tell the session to stop and wait for it. When it has not ended in time the run stays open and the next look waits again.</summary>
    private async Task StopRunAsync(IServiceRunStore runs, string reason, string endReason, CancellationToken cancellationToken)
    {
        var running = _running!;
        running.StopReason ??= reason;
        running.EndReason ??= reason == OptionChainRecorderService.WindowEnded ? RecorderEndReasons.WindowEnd : endReason;
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
            OptionChainLog.StopTimedOut(logger, running.Run.Id);
            return;
        }

        await EndRunAsync(runs, clock.GetUtcNow(), cancellationToken);
        _waiting = null;
    }

    /// <summary>Writes the end row of the day once the day is over (a new India day, or the service window has closed) and forgets the day.</summary>
    private async Task FinishDayIfOverAsync(IOptionChainStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_day is not { } day)
        {
            return;
        }

        var ist = IndiaTime.ToIst(now);
        if (IndiaTime.Today(now) == day.Date && TimeOnly.FromTimeSpan(ist.TimeOfDay) < _dayOver)
        {
            return;
        }

        var stored = await SafeStoredBytesAsync(store, cancellationToken);
        var note = session.FreeSpaceReadable ? null : "Free disk space could not be read; only the size cap was in force.";
        await store.RecordSessionAsync(new ChainSessionRecord(day.Date, day.StartedAt, now, day.Last ?? RecorderEndReasons.WindowEnd, day.Slots, day.Requests, day.Ok, day.Gaps, day.Rows, stored, note), cancellationToken);
        _day = null;
    }

    private static async Task<long?> SafeStoredBytesAsync(IOptionChainStore store, CancellationToken cancellationToken)
    {
        try
        {
            return await store.StoredBytesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the size is a note on the row, not a reason to lose the row
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private Day DayOf(DateOnly date, DateTimeOffset now)
    {
        if (_day is null || _day.Date != date)
        {
            _day = new Day(date, now);
        }

        return _day;
    }

    private async Task CloseRunAsync(IServiceRunStore runs, ServiceRun run, ServiceRunStatus status, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await runs.CompleteAsync(run.Id, status, ServiceRun.Limit(message), now, cancellationToken);
            var code = status.ToCode();
            OptionChainLog.RunClosed(logger, run.Id, code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a run record that cannot be closed now is closed as interrupted at the next start
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionChainLog.RunNotClosed(logger, run.Id, ex);
        }
    }

    /// <summary>The heartbeat, from a scope of its own; one that cannot be written is logged and the loop goes on.</summary>
    private async Task ReportAsync(string state, CancellationToken cancellationToken)
    {
        try
        {
            var stats = _running is not null ? session.Stats : new ChainRunStats(0, 0, 0, 0, 0);
            var details = new ChainHeartbeat(settings.Enabled, (_day?.Slots ?? 0) + stats.Slots, (_day?.Requests ?? 0) + stats.Requests, (_day?.Ok ?? 0) + stats.Ok, (_day?.Gaps ?? 0) + stats.Gaps, (_day?.Rows ?? 0) + stats.Rows, session.FreeSpaceReadable);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IOptionChainHeartbeats>().ReportAsync(state, details, application.Instance, application.Version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the next look reports again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionChainLog.SupervisorFailed(logger, ex);
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
                    await StopRunAsync(runs, OptionChainRecorderService.Stopped, RecorderEndReasons.Paused, wait.Token);
                }
            }

            await scope.ServiceProvider.GetRequiredService<IOptionChainHeartbeats>().ReportAsync(RecorderStates.Stopped, new ChainHeartbeat(settings.Enabled, 0, 0, 0, 0, 0, session.FreeSpaceReadable), application.Instance, application.Version, wait.Token);
        }
#pragma warning disable CA1031 // the process is going down; the record is closed as interrupted at the next start if this fails
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OptionChainLog.SupervisorFailed(logger, ex);
        }
    }

    private sealed class Day(DateOnly date, DateTimeOffset startedAt)
    {
        public DateOnly Date { get; } = date;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public int Slots { get; set; }

        public int Requests { get; set; }

        public int Ok { get; set; }

        public int Gaps { get; set; }

        public long Rows { get; set; }

        public string? Last { get; set; }
    }

    private sealed class Running(ServiceRun run, CancellationTokenSource stop, Task<ChainSessionResult> task, DateTimeOffset startedAt, string fingerprint)
    {
        public ServiceRun Run { get; } = run;

        public CancellationTokenSource Stop { get; } = stop;

        public Task<ChainSessionResult> Task { get; } = task;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public string Fingerprint { get; } = fingerprint;

        public string? StopReason { get; set; }

        public string? EndReason { get; set; }
    }
}
