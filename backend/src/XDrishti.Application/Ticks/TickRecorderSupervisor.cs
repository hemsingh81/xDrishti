using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Ticks;

/// <summary>
/// The trade-tick recorder's own run owner (P7-16b criteria 5, 6, 8, 9), beside the live feed's and the depth recorder's supervisors and never in their way: its own try/catch, its own run record (<c>tick-recorder</c>), its own heartbeat row
/// (<c>xd-ticks</c>). Every <see cref="Interval"/> (15 s) it decides whether recording should be up. <b>All three gates must be open</b>: the deployment flag <c>Ticks:Enabled</c>, the owner's stored switch (newest row ON; no row = OFF) and the service
/// <c>tick-recorder</c> (enabled, inside its window on a trading day). With any gate closed nothing reads the credentials, builds a URL or opens a socket. Before each start the disk guard is asked (the recorder's own cap <c>Ticks:MaxStoredGb</c>,
/// the free floor <c>Depth:MinFreeGb</c> shared with the depth recorder); the day's set (<see cref="TickSetChoice"/>) is stored once in the tick recorder's own table and reused for a restart of the same day. The <c>market.tick_sessions</c> row is
/// written when the day is over; a day with a stored tick set and no end row is closed as <c>crashed</c> at the next start. It never writes a depth table. The app never deletes data.
/// </summary>
internal sealed class TickRecorderSupervisor(IServiceScopeFactory scopes, ITickSession session, TickSettings settings, DepthSettings depth, IApplicationInfo application, TimeProvider clock, ILogger<TickRecorderSupervisor> logger) : ITickSupervisor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(8);

    public static readonly TimeSpan TokenCheckInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan CrashPause = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);

    public const string FlappingMessage = "Flapping: more than 10 reconnects in 5 minutes; attempts are paused for 5 minutes.";

    public const string TokenRefusedMessage = "Token refused: Dhan refused the Data account's token for the tick socket; waiting for a new token.";

    public const string NotSubscribedMessage = "Not subscribed: Dhan answered that the Data APIs are not part of the plan (806); waiting for new credentials.";

    public const string NoTokenLostMessage = "No usable token: the Data account is not connected any more.";

    public const string DiskBudgetMessage = "Disk budget reached: the stored trade ticks are over their cap. Recording stopped; nothing was deleted.";

    public const string DiskFreeMessage = "Low free disk space: recording stopped; nothing was deleted.";

    public const string CrashMessage = "The trade-tick recorder stopped unexpectedly. See the xd-feed log.";

    public const string InterruptedMessage = "Interrupted: the feed host restarted while the window was open.";

    public const string NoTokenMessage = "No usable token";

    public const string NoInstrumentsMessage = "No instruments to record";

    public const string CrashedNote = "The process ended before the day was closed.";

    private readonly TimeOnly _dayOver = TickRecorderService.WindowEnd;
    private Running? _running;
    private Day? _day;
    private DateTimeOffset? _startedAt;
    private bool _recovered;
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextTokenCheck = DateTimeOffset.MinValue;
    private bool _tokenRefused;
    private string? _refusedFingerprint;
    private string _refusedState = DepthStates.TokenRefused;
    private string? _waiting;
    private DateOnly? _skippedOn;
    private bool _freeSpaceReadable = true;
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
        var state = _waiting ?? DepthStates.Closed;
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
            TickLog.SupervisorFailed(logger, ex);
        }

        await ReportAsync(state, cancellationToken);
    }

    private async Task<string> LookAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            // Gate 1: nothing below this line runs - no credentials, no URL, no socket.
            return DepthStates.Disabled;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var runs = services.GetRequiredService<IServiceRunStore>();
        var store = services.GetRequiredService<ITickStore>();
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
            switchOn = (await services.GetRequiredService<ITickSwitchStore>().CurrentAsync(cancellationToken))?.Enabled == true;
            service = (await services.GetRequiredService<ServiceSettingsReader>().ReadAsync(TickRecorderService.Definition, cancellationToken)).Settings;
            calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // A gate that cannot be read is a closed gate: a running session is stopped, nothing new is started. Only the type of the exception is logged.
            TickLog.GateReadFailed(logger, ex.GetType().Name);
            if (_running is { Task.IsCompleted: true })
            {
                await EndRunAsync(runs, now, cancellationToken);
            }

            if (_running is not null)
            {
                await StopRunAsync(runs, TickRecorderService.Paused, DepthEndReasons.Paused, cancellationToken);
            }

            return DepthStates.Paused;
        }

        var decision = FeedWindow.Decide(service, calendar, now, null);
        var shouldBeUp = switchOn && decision.ShouldBeUp;

        if (_running is { Task.IsCompleted: true })
        {
            await EndRunAsync(runs, now, cancellationToken);
        }

        if (_running is not null && !shouldBeUp)
        {
            await StopRunAsync(runs, !switchOn || decision.DownState == FeedStates.Paused ? TickRecorderService.Paused : TickRecorderService.WindowEnded, DepthEndReasons.Paused, cancellationToken);
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
            return _waiting ?? DepthStates.Connecting;
        }

        return !switchOn ? DepthStates.SwitchOff : decision.DownState == FeedStates.Paused ? DepthStates.Paused : DepthStates.Closed;
    }

    /// <summary>The first look of a process: the run the previous process left open is closed as failed (interrupted); a day with a stored set and no end row is closed as crashed.</summary>
    private async Task RecoverAsync(IServiceRunStore runs, ITickStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await runs.GetLatestAsync([TickRecorderService.Id], cancellationToken);
        if (latest.TryGetValue(TickRecorderService.Id, out var run) && run.IsRunning && run.StartedAt < _startedAt)
        {
            await runs.CompleteAsync(run.Id, ServiceRunStatus.Failed, InterruptedMessage, now, cancellationToken);
        }

        // Today's date is skipped: a restart in the middle of the day must not take the day's real end row away (it is written once, the first wins). A day that is never closed is found tomorrow.
        var today = IndiaTime.Today(now);
        var days = (await store.FindUnfinishedDaysAsync(cancellationToken)).Where(d => d < today).ToList();
        foreach (var date in days)
        {
            var stored = await SafeStoredBytesAsync(store, cancellationToken);
            var set = await store.LoadSetAsync(date, cancellationToken);
            await store.RecordSessionAsync(new TickSessionRecord(date, now, now, DepthEndReasons.Crashed, set.Count, 0, 0, 0, 0, 0, stored, CrashedNote), cancellationToken);
        }

        if (days.Count > 0)
        {
            TickLog.CrashedDaysClosed(logger, days.Count);
        }
    }

    private async Task TryOpenRunAsync(IServiceProvider services, IServiceRunStore runs, ITickStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_diskLatched || now < _resumeAt || (_tokenRefused && now < _nextTokenCheck))
        {
            return;
        }

        var today = IndiaTime.Today(now);

        // Before each start: the disk guard (a stopped recorder stays stopped while the condition holds).
        var stored = await store.StoredBytesAsync(cancellationToken);
        var free = await store.FreeBytesAsync(cancellationToken);
        _freeSpaceReadable = free is not null;
        var verdict = DepthDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes);
        if (verdict is DepthDiskVerdict.OverBudget or DepthDiskVerdict.LowFree)
        {
            var over = verdict == DepthDiskVerdict.OverBudget;
            _diskLatched = true;
            _waiting = over ? DepthStates.DiskBudget : DepthStates.DiskFree;
            await SkipOncePerDayAsync(runs, today, over ? DiskBudgetMessage : DiskFreeMessage, now, cancellationToken);
            DayOf(today, now).Last = over ? DepthEndReasons.DiskBudget : DepthEndReasons.DiskFree;
            return;
        }

        var set = await BuildOrLoadSetAsync(services, store, today, now, cancellationToken);
        if (set is null)
        {
            _waiting = TickSetChoice.WaitingForSet;
            return;
        }

        if (set.Count == 0)
        {
            _waiting = DepthStates.NoInstruments;
            await SkipOncePerDayAsync(runs, today, NoInstrumentsMessage, now, cancellationToken);
            return;
        }

        var credentials = await services.GetRequiredService<IDataAccountCredentials>().GetCredentialsAsync(cancellationToken);
        if (_tokenRefused)
        {
            _nextTokenCheck = now + TokenCheckInterval;
        }

        if (credentials is null)
        {
            _waiting = _tokenRefused ? _refusedState : DepthStates.NoToken;
            if (!_tokenRefused)
            {
                await SkipOncePerDayAsync(runs, today, NoTokenMessage, now, cancellationToken);
            }

            return;
        }

        if (_tokenRefused)
        {
            if (_refusedFingerprint == FeedCredentialFingerprint.Of(credentials))
            {
                _waiting = _refusedState;
                return;
            }

            _tokenRefused = false;
            _refusedFingerprint = null;
        }

        var run = ServiceRun.Start(TickRecorderService.Id, ServiceRunTrigger.Scheduled, 1, now);
        if (!await runs.TryStartAsync(run, StaleAfter, cancellationToken))
        {
            TickLog.RunNotOpened(logger);
            return;
        }

        var day = DayOf(today, now);
        day.Instruments = set.Count;
        var stop = new CancellationTokenSource();
        _running = new Running(run, stop, session.RunAsync(set, stop.Token), now);
        _waiting = null;
        TickLog.RunOpened(logger, run.Id);
    }

    /// <summary>
    /// The day's tick set: the stored one when the day has one (a restart keeps it); else a copy of the depth recorder's stored set, or - when none is stored by 09:14:30 - one built the same way from the read-only inputs; stored in the tick
    /// recorder's own table. Null while it waits for the depth set.
    /// </summary>
    private async Task<IReadOnlyList<TickSetEntry>?> BuildOrLoadSetAsync(IServiceProvider services, ITickStore store, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stored = await store.LoadSetAsync(today, cancellationToken);
        if (stored.Count > 0)
        {
            TickLog.SetReady(logger, stored.Count, stored[0].Source, 0, true);
            return stored;
        }

        var depthSet = await store.LoadDepthSetAsync(today, cancellationToken);
        IReadOnlyList<TickSetEntry> chosen;
        var unmapped = 0;
        switch (TickSetChoice.Decide(now, depthSet.Count > 0))
        {
            case TickSetAction.Wait:
                return null;
            case TickSetAction.UseDepthSet:
                chosen = depthSet;
                break;
            default:
                var armed = await services.GetRequiredService<IArmedTicketReader>().ListAsync(today, cancellationToken);
                var liquid = await services.GetRequiredService<IDepthUniverse>().MostLiquidAsync(today, depth.MaxInstruments, cancellationToken);
                var symbols = armed.Select(a => a.Symbol).Concat(depth.PinnedSymbols).Distinct(StringComparer.Ordinal).ToList();
                var securities = await services.GetRequiredService<IInstrumentSecurities>().FindAsync(symbols, cancellationToken);
                var built = DepthRecordingSet.Build(armed, depth.PinnedSymbols, liquid, securities, depth.MaxInstruments, depth.MaxArmed);
                chosen = TickSetChoice.FromBuilt(built.Entries);
                unmapped = built.Unmapped;
                break;
        }

        if (chosen.Count > 0)
        {
            await store.SaveSetAsync(today, chosen, cancellationToken);
        }

        TickLog.SetReady(logger, chosen.Count, chosen.Count > 0 ? chosen[0].Source : TickSources.Built, unmapped, false);
        return chosen;
    }

    private async Task SkipOncePerDayAsync(IServiceRunStore runs, DateOnly today, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_skippedOn == today)
        {
            return;
        }

        _skippedOn = today;
        await runs.RecordAsync(ServiceRun.Skipped(TickRecorderService.Id, ServiceRunTrigger.Scheduled, message, now), cancellationToken);
    }

    /// <summary>The session ended by itself: close the run with what happened, remember the end for the day and set up the wait that follows.</summary>
    private async Task EndRunAsync(IServiceRunStore runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = _running!;
        TickSessionResult? result = null;
        try
        {
            result = await running.Task;
        }
#pragma warning disable CA1031 // a crashed session fails its run; the details are in the log
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.SessionCrashed(logger, ex);
        }

        ServiceRunStatus status;
        string message;
        string reason;
        switch (result?.End)
        {
            case DepthSessionEnd.TokenRefused:
                (status, message, reason) = (ServiceRunStatus.Failed, TokenRefusedMessage, DepthEndReasons.TokenRefused);
                Refused(result, now, DepthStates.TokenRefused);
                break;
            case DepthSessionEnd.NotSubscribed:
                (status, message, reason) = (ServiceRunStatus.Failed, NotSubscribedMessage, DepthEndReasons.NotSubscribed);
                Refused(result, now, DepthStates.NotSubscribed);
                break;
            case DepthSessionEnd.Flapping:
                (status, message, reason) = (ServiceRunStatus.Failed, FlappingMessage, DepthEndReasons.Flapping);
                _resumeAt = now + ReconnectLimiter.Pause;
                _waiting = DepthStates.Flapping;
                break;
            case DepthSessionEnd.NoToken:
                (status, message, reason) = (ServiceRunStatus.Failed, NoTokenLostMessage, DepthEndReasons.TokenRefused);
                _waiting = DepthStates.NoToken;
                break;
            case DepthSessionEnd.DiskBudget:
                (status, message, reason) = (ServiceRunStatus.Failed, DiskBudgetMessage, DepthEndReasons.DiskBudget);
                _diskLatched = true;
                _waiting = DepthStates.DiskBudget;
                break;
            case DepthSessionEnd.DiskFree:
                (status, message, reason) = (ServiceRunStatus.Failed, DiskFreeMessage, DepthEndReasons.DiskFree);
                _diskLatched = true;
                _waiting = DepthStates.DiskFree;
                break;
            case DepthSessionEnd.Stopped:
                (status, message, reason) = (ServiceRunStatus.Succeeded, running.StopReason ?? TickRecorderService.Stopped, running.EndReason ?? DepthEndReasons.WindowEnd);
                break;
            default:
                (status, message, reason) = (ServiceRunStatus.Failed, CrashMessage, DepthEndReasons.Crashed);
                _resumeAt = now + CrashPause;
                _waiting = DepthStates.Connecting;
                break;
        }

        var stats = result?.Stats ?? session.Stats;
        var day = DayOf(IndiaTime.Today(running.StartedAt), running.StartedAt);
        day.Packets += stats.Packets;
        day.Rows += stats.Rows;
        day.Unchanged += stats.Unchanged;
        day.Dropped += stats.DroppedRows;
        day.Gap += stats.GapSeconds;
        day.Last = reason;
        await CloseRunAsync(runs, running.Run, status, message, now, cancellationToken);
        running.Stop.Dispose();
        _running = null;
    }

    private void Refused(TickSessionResult result, DateTimeOffset now, string word)
    {
        _tokenRefused = true;
        _refusedState = word;
        _refusedFingerprint = result.CredentialFingerprint;
        _nextTokenCheck = now + TokenCheckInterval;
        _waiting = word;
    }

    /// <summary>A gate closed, the window ended or the process is stopping: tell the session to stop and wait for it. When it has not ended in time the run stays open and the next look waits again.</summary>
    private async Task StopRunAsync(IServiceRunStore runs, string reason, string endReason, CancellationToken cancellationToken)
    {
        var running = _running!;
        running.StopReason ??= reason;
        running.EndReason ??= reason == TickRecorderService.WindowEnded ? DepthEndReasons.WindowEnd : endReason;
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
            TickLog.StopTimedOut(logger, running.Run.Id);
            return;
        }

        await EndRunAsync(runs, clock.GetUtcNow(), cancellationToken);
        _waiting = null;
    }

    /// <summary>Writes the end row of the day once the day is over (a new India day, or the service window has closed) and forgets the day.</summary>
    private async Task FinishDayIfOverAsync(ITickStore store, DateTimeOffset now, CancellationToken cancellationToken)
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
        var note = _freeSpaceReadable ? null : "Free disk space could not be read; only the size cap was in force.";
        await store.RecordSessionAsync(new TickSessionRecord(day.Date, day.StartedAt, now, day.Last ?? DepthEndReasons.WindowEnd, day.Instruments, day.Packets, day.Rows, day.Unchanged, day.Dropped, (int)day.Gap, stored, note), cancellationToken);
        _day = null;
    }

    private static async Task<long?> SafeStoredBytesAsync(ITickStore store, CancellationToken cancellationToken)
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
            TickLog.RunClosed(logger, run.Id, code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a run record that cannot be closed now is closed as interrupted at the next start
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.RunNotClosed(logger, run.Id, ex);
        }
    }

    /// <summary>The heartbeat, from a scope of its own; one that cannot be written is logged and the loop goes on.</summary>
    private async Task ReportAsync(string state, CancellationToken cancellationToken)
    {
        try
        {
            var stats = _running is not null ? session.Stats : new TickRunStats(0, 0, 0, 0, 0, 0);
            var details = new TickHeartbeat(settings.Enabled, _day?.Instruments ?? 0, (_day?.Packets ?? 0) + stats.Packets, (_day?.Rows ?? 0) + stats.Rows, (_day?.Unchanged ?? 0) + stats.Unchanged, (_day?.Dropped ?? 0) + stats.DroppedRows, (int)((_day?.Gap ?? 0) + stats.GapSeconds), stats.Reconnects, _freeSpaceReadable);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITickHeartbeats>().ReportAsync(state, details, application.Instance, application.Version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the next look reports again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.SupervisorFailed(logger, ex);
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
                    await StopRunAsync(runs, TickRecorderService.Stopped, DepthEndReasons.Paused, wait.Token);
                }
            }

            await scope.ServiceProvider.GetRequiredService<ITickHeartbeats>().ReportAsync(DepthStates.Stopped, new TickHeartbeat(settings.Enabled, 0, 0, 0, 0, 0, 0, 0, _freeSpaceReadable), application.Instance, application.Version, wait.Token);
        }
#pragma warning disable CA1031 // the process is going down; the record is closed as interrupted at the next start if this fails
        catch (Exception ex)
#pragma warning restore CA1031
        {
            TickLog.SupervisorFailed(logger, ex);
        }
    }

    private sealed class Day(DateOnly date, DateTimeOffset startedAt)
    {
        public DateOnly Date { get; } = date;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public int Instruments { get; set; }

        public long Packets { get; set; }

        public long Rows { get; set; }

        public long Unchanged { get; set; }

        public long Dropped { get; set; }

        public long Gap { get; set; }

        public string? Last { get; set; }
    }

    private sealed class Running(ServiceRun run, CancellationTokenSource stop, Task<TickSessionResult> task, DateTimeOffset startedAt)
    {
        public ServiceRun Run { get; } = run;

        public CancellationTokenSource Stop { get; } = stop;

        public Task<TickSessionResult> Task { get; } = task;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public string? StopReason { get; set; }

        public string? EndReason { get; set; }
    }
}
