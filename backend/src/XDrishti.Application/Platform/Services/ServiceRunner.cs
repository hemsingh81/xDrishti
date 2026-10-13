using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// Starts services and sees their runs through (ADR 0018 §3, §5): the one path of the scheduler, of catch-up, of run-after triggers and of Run now. A service runs <b>at most once at a time</b>: a start while
/// it runs (or waits for a retry) is recorded as a skipped run and starts nothing. Every attempt is a run record; an attempt that fails is retried up to the service's <c>retries</c> with the back-off
/// 30 s, 2 min, 10 min, 30 min, 1 h (the scheduler starts the retry when it is due, see <see cref="StartDueRetriesAsync"/>, and takes the sequence up again after a restart, see <see cref="ResumeRetry"/>). An
/// attempt past its timeout is cancelled and recorded as failed (timed out): when the work was handed to a background worker that cannot be stopped, the record says the job is still running; a timeout is never
/// retried. A run that succeeds starts the services set to run after it. Settings are read again for every attempt, so an edit counts at once. The run records are written under the application's own token, never
/// under the token of the request that asked for the run: a client that disconnects cannot leave a half-made start.
/// </summary>
public sealed class ServiceRunner(IServiceScopeFactory scopes, ServiceRegistry registry, TimeProvider clock, ILogger<ServiceRunner> logger) : IDisposable
{
    /// <summary>Recorded as the requester of runs the scheduler starts.</summary>
    public const string SchedulerRequester = "system:scheduler";

    /// <summary>Recorded as the requester of attempts that continue a sequence of retries after a restart.</summary>
    public const string RetryRequester = "system:retry";

    public const string AlreadyRunningMessage = "Not started: the service was running already.";

    /// <summary>The wait before the next attempt, by the number of the attempt that failed.</summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1)];

    /// <summary>A run still marked running after this long cannot be running (the longest timeout is 240 minutes); it is closed as interrupted when the service is next started.</summary>
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(5);

    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Sequence> _active = new(StringComparer.Ordinal);

    /// <summary>The earliest time a waiting retry is due; null when none waits.</summary>
    public DateTimeOffset? NextRetryAt
    {
        get
        {
            lock (_gate)
            {
                return _active.Values.Where(s => s.RetryAt is not null).Select(s => s.RetryAt).Min();
            }
        }
    }

    /// <summary>Whether the service has a run or a retry in progress in this process.</summary>
    public bool IsActive(string serviceId)
    {
        lock (_gate)
        {
            return _active.ContainsKey(serviceId);
        }
    }

    /// <summary>
    /// Starts one run of the service in the background and returns at once with its run record. When the service is running already (or waits for a retry) nothing starts and a skipped run is recorded
    /// (<see cref="ServiceStart.AlreadyRunning"/>). With <paramref name="dropWhenBusy"/> (the ticks of an interval or window schedule, ADR 0019 §5) a start that finds the service busy is only logged
    /// (<c>service.tick_dropped</c>) and leaves no run record. <paramref name="cancellationToken"/> only matters before the start: once the run record is written the work goes on whatever happens to the caller.
    /// </summary>
    public async ValueTask<ServiceStart> StartAsync(string serviceId, ServiceRunTrigger trigger, string requestedBy, CancellationToken cancellationToken, bool dropWhenBusy = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registry.Find(serviceId) is not { } service)
        {
            return ServiceStart.NoSuchService;
        }

        var sequence = new Sequence(trigger, requestedBy);
        if (!TryReserve(serviceId, sequence))
        {
            return await BusyAsync(serviceId, trigger, dropWhenBusy);
        }

        try
        {
            if (await BeginAsync(service, sequence, 1) is { } run)
            {
                return ServiceStart.Started(run);
            }
        }
#pragma warning disable CA1031 // the reservation must be given back whatever went wrong
        catch (Exception)
#pragma warning restore CA1031
        {
            Release(serviceId);
            throw;
        }

        Release(serviceId);
        return await BusyAsync(serviceId, trigger, dropWhenBusy);
    }

    private async ValueTask<ServiceStart> BusyAsync(string serviceId, ServiceRunTrigger trigger, bool dropWhenBusy)
    {
        if (dropWhenBusy)
        {
            var triggerCode = trigger.ToCode();
            ServicesLog.TickDropped(logger, serviceId, triggerCode);
            return ServiceStart.Busy(null);
        }

        return ServiceStart.Busy(await RecordSkippedAsync(serviceId, trigger, AlreadyRunningMessage));
    }

    /// <summary>
    /// Takes up a sequence of retries again after a restart: the failed attempt before it is the one before <paramref name="nextAttempt"/>, and the next attempt is due at <paramref name="dueAt"/> (it is started by
    /// <see cref="StartDueRetriesAsync"/> like any retry). False when the service is active already.
    /// </summary>
    public bool ResumeRetry(string serviceId, ServiceRunTrigger trigger, string requestedBy, int nextAttempt, DateTimeOffset dueAt)
    {
        var resumed = TryReserve(serviceId, new Sequence(trigger, requestedBy) { NextAttempt = nextAttempt, RetryAt = dueAt });
        if (resumed)
        {
            ServicesLog.RetryResumed(logger, serviceId, nextAttempt, dueAt);
        }

        return resumed;
    }

    /// <summary>Starts the retries that are due. A retry of a service that has been paused meanwhile is dropped, one that finds the service running is dropped with a skipped run. Called by the scheduler on every look.</summary>
    public async Task StartDueRetriesAsync(CancellationToken cancellationToken)
    {
        List<(string Id, Sequence Sequence)> due;
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            due = [.. _active.Where(a => a.Value.RetryAt is { } at && at <= now && a.Value.Task is null).Select(a => (a.Key, a.Value))];
            foreach (var (_, sequence) in due)
            {
                sequence.RetryAt = null;
            }
        }

        foreach (var (id, sequence) in due)
        {
            try
            {
                await StartRetryAsync(id, sequence, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Release(id);
                throw;
            }
#pragma warning disable CA1031 // one failed retry must not stop the others or the scheduler
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ServicesLog.Crashed(logger, id, ex);
                Release(id);
            }
        }
    }

    /// <summary>Completes when every run that is executing now has ended (retries that wait are not waited for). For tests and for shutdown.</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return Task.WhenAll(_active.Values.Select(s => s.Task).OfType<Task>());
        }
    }

    /// <summary>Tells the runs in progress that the application is stopping and waits for them, at most <paramref name="wait"/>.</summary>
    public async Task StopAsync(TimeSpan wait)
    {
        await _stopping.CancelAsync();
        try
        {
            await WhenIdleAsync().WaitAsync(wait, clock);
        }
        catch (TimeoutException)
        {
            // the runs did not end in time; their records are closed as interrupted at the next start
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task StartRetryAsync(string id, Sequence sequence, CancellationToken cancellationToken)
    {
        if (registry.Find(id) is not { } service)
        {
            Release(id);
            return;
        }

        bool enabled;
        await using (var scope = scopes.CreateAsyncScope())
        {
            enabled = (await scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>().ReadAsync(id, cancellationToken)).Settings.Enabled;
        }

        if (!enabled)
        {
            ServicesLog.RetryDropped(logger, id, sequence.NextAttempt, "the service was paused");
            Release(id);
            return;
        }

        if (await BeginAsync(service, sequence, sequence.NextAttempt) is null)
        {
            ServicesLog.RetryDropped(logger, id, sequence.NextAttempt, "the service was running already");
            Release(id);
            await RecordSkippedAsync(id, sequence.Trigger, "Retry not started: the service was running already.");
        }
    }

    private bool TryReserve(string serviceId, Sequence sequence)
    {
        lock (_gate)
        {
            return _active.TryAdd(serviceId, sequence);
        }
    }

    private void Release(string serviceId)
    {
        lock (_gate)
        {
            _active.Remove(serviceId);
        }
    }

    /// <summary>Records the attempt as running and executes it in the background; null when the service has a running run in the database (another process, or a record not closed yet).</summary>
    private async ValueTask<ServiceRun?> BeginAsync(IScheduledService service, Sequence sequence, int attempt)
    {
        var id = service.Definition.Id;
        ServiceSettings settings;
        ServiceRun run;
        await using (var scope = scopes.CreateAsyncScope())
        {
            settings = await ReadSettingsAsync(scope, service, _stopping.Token);
            run = ServiceRun.Start(id, sequence.Trigger, attempt, clock.GetUtcNow());
            if (!await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().TryStartAsync(run, StaleAfter, _stopping.Token))
            {
                return null;
            }
        }

        var triggerCode = sequence.Trigger.ToCode();
        ServicesLog.Started(logger, id, run.Id, triggerCode, attempt);
        lock (_gate)
        {
            sequence.NextAttempt = attempt + 1;
            sequence.Task = Task.Run(() => ExecuteAsync(service, sequence, run, settings), CancellationToken.None);
        }

        return run;
    }

    private async Task ExecuteAsync(IScheduledService service, Sequence sequence, ServiceRun run, ServiceSettings settings)
    {
        var id = service.Definition.Id;
        var retry = false;
        var triggerCode = sequence.Trigger.ToCode();
        try
        {
            var attempt = await InvokeAsync(service, sequence, run, settings);
            var outcome = attempt.Outcome;
            await CloseAsync(run, outcome);
            if (outcome.Status == ServiceRunStatus.Failed)
            {
                ServicesLog.Failed(logger, id, run.Id, run.Attempt, outcome.Message ?? string.Empty);
                retry = !attempt.Stopping && !attempt.TimedOut && await PlanRetryAsync(service, sequence, run.Attempt);
            }
            else if (outcome.Status == ServiceRunStatus.Skipped)
            {
                ServicesLog.Skipped(logger, id, triggerCode, outcome.Message ?? string.Empty);
            }
            else
            {
                var statusCode = outcome.Status.ToCode();
                ServicesLog.Finished(logger, id, run.Id, statusCode);
            }

            if (!retry)
            {
                Release(id);
                if (outcome.Status == ServiceRunStatus.Succeeded)
                {
                    await StartDependentsAsync(id);
                }
            }
        }
#pragma warning disable CA1031 // nothing a run does may leave the service marked as busy in this process
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.Crashed(logger, id, ex);
            Release(id);
        }
    }

    private async Task<AttemptResult> InvokeAsync(IScheduledService service, Sequence sequence, ServiceRun run, ServiceSettings settings)
    {
        var timeout = TimeSpan.FromMinutes(Math.Clamp(settings.TimeoutMinutes, ServiceSettings.MinTimeoutMinutes, ServiceSettings.MaxTimeoutMinutes));
        using var timeoutSource = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, timeoutSource.Token);
        var context = new ServiceRunContext(run.Id, sequence.Trigger, run.Attempt, sequence.RequestedBy, service.Definition.WithDefaults(settings.Parameters), LinkDataJobAsync(run.Id));
        try
        {
            return new AttemptResult(await service.RunAsync(context, linked.Token), false, false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !_stopping.IsCancellationRequested)
        {
            // The work may be in a background worker that this cancellation cannot stop: then the record says so, and the next start refuses to pile a second job on it.
            var message = $"{ServiceRun.TimedOutPrefix} after {timeout.TotalMinutes:0} min{(context.HandedOffToBackground ? " (the job is still running)" : string.Empty)}.";
            return new AttemptResult(ServiceOutcome.Failed(message), false, true);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return new AttemptResult(ServiceOutcome.Failed($"{ServiceRun.InterruptedPrefix}: the application was stopping."), true, false);
        }
#pragma warning disable CA1031 // a service that throws fails its run; the details are in the log, not in the record
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.Crashed(logger, service.Definition.Id, ex);
            return new AttemptResult(ServiceOutcome.Failed("The service failed unexpectedly. See the API log."), false, false);
        }
    }

    private Func<Guid, CancellationToken, ValueTask> LinkDataJobAsync(Guid runId) => async (dataJobId, cancellationToken) =>
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().LinkDataJobAsync(runId, dataJobId, cancellationToken);
    };

    /// <summary>Ends the run record. A failure here must not be silent: the record would block the service until it is closed as stale.</summary>
    private async ValueTask CloseAsync(ServiceRun run, ServiceOutcome outcome)
    {
        using var wait = new CancellationTokenSource(CloseWait, clock);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().CompleteAsync(run.Id, outcome.Status, ServiceRun.Limit(outcome.Message), clock.GetUtcNow(), wait.Token);
        }
#pragma warning disable CA1031 // see the summary
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.CloseFailed(logger, run.ServiceId, run.Id, ex);
        }
    }

    /// <summary>Whether the failed attempt is retried: it is when the service's retries (read now) allow another one; the retry is then due after the back-off.</summary>
    private async ValueTask<bool> PlanRetryAsync(IScheduledService service, Sequence sequence, int failedAttempt)
    {
        int retries;
        await using (var scope = scopes.CreateAsyncScope())
        {
            retries = (await ReadSettingsAsync(scope, service, _stopping.Token)).Retries;
        }

        if (failedAttempt > Math.Min(retries, ServiceSettings.MaxRetries))
        {
            return false;
        }

        var due = clock.GetUtcNow() + Backoff[Math.Min(failedAttempt, Backoff.Count) - 1];
        lock (_gate)
        {
            sequence.Task = null;
            sequence.RetryAt = due;
        }

        ServicesLog.Retry(logger, service.Definition.Id, failedAttempt + 1, due);
        return true;
    }

    private async Task StartDependentsAsync(string serviceId)
    {
        try
        {
            IReadOnlyList<EffectiveService> all;
            await using (var scope = scopes.CreateAsyncScope())
            {
                all = await scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>().ReadAllAsync(_stopping.Token);
            }

            foreach (var dependent in all.Where(s => s.Settings is { Enabled: true, Schedule: { Type: ScheduleType.After } schedule } && schedule.After == serviceId))
            {
                await StartAsync(dependent.Id, ServiceRunTrigger.After, $"system:after:{serviceId}", _stopping.Token);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // stopping
        }
#pragma warning disable CA1031 // a dependent that cannot start must not undo the run that succeeded
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.DependentFailed(logger, serviceId, ex);
        }
    }

    private static async ValueTask<ServiceSettings> ReadSettingsAsync(AsyncServiceScope scope, IScheduledService service, CancellationToken cancellationToken) =>
        (await scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>().ReadAsync(service.Definition.Id, cancellationToken)).Settings;

    private async ValueTask<ServiceRun?> RecordSkippedAsync(string serviceId, ServiceRunTrigger trigger, string message)
    {
        var triggerCode = trigger.ToCode();
        ServicesLog.Skipped(logger, serviceId, triggerCode, message);
        try
        {
            var skipped = ServiceRun.Skipped(serviceId, trigger, message, clock.GetUtcNow());
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().RecordAsync(skipped, _stopping.Token);
            return skipped;
        }
#pragma warning disable CA1031 // the refusal stands even when it cannot be written down
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.Crashed(logger, serviceId, ex);
            return null;
        }
    }

    private sealed record AttemptResult(ServiceOutcome Outcome, bool Stopping, bool TimedOut);

    /// <summary>The run of one service in progress: what started it, the next attempt, the task of the attempt that executes (null while it waits for a retry) and when the retry is due.</summary>
    private sealed class Sequence(ServiceRunTrigger trigger, string requestedBy)
    {
        public ServiceRunTrigger Trigger { get; } = trigger;

        public string RequestedBy { get; } = requestedBy;

        public int NextAttempt { get; set; } = 1;

        public Task? Task { get; set; }

        public DateTimeOffset? RetryAt { get; set; }
    }
}
