using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The one scheduler of the automatic jobs (ADR 0018 §3, §4). At start-up it closes the runs the previous process left running (<c>failed: interrupted</c>), stores the first-start settings of the
/// services that have no row, and makes up once for each service whose scheduled time passed while the application was down (trigger <c>catch_up</c>; never for a paused service, never more than one
/// per service per start). Then it looks at the schedules at least once a minute and at the next start: it re-reads the stored rows each time, so an edit counts at the next look without a restart,
/// starts the services whose time came since the last look, starts the retries that are due and leaves the rest to <see cref="ServiceRunner"/>. A paused service is not started, and its past times
/// are not made up for when it is resumed. The ticks of an interval or window schedule are never made up for, and one that falls while the service still runs is dropped with a log line and no run record
/// (ADR 0019 §5). The API's hosted service only calls <see cref="RunAsync"/>.
/// </summary>
public sealed class ServiceScheduler(
    IServiceScopeFactory scopes,
    ServiceRegistry registry,
    ServiceRunner runner,
    IApplicationInfo application,
    TimeProvider clock,
    ILogger<ServiceScheduler> logger)
{
    /// <summary>The longest single wait; the clock and the schedules are looked at again after each.</summary>
    public static readonly TimeSpan MaxSlice = TimeSpan.FromMinutes(1);

    /// <summary>A scheduled time that is found to be later than this when the scheduler looks has passed unseen (the machine slept): it is made up for by the catch-up rules instead of started as usual.</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);

    /// <summary>How long a stopping application waits for the runs in progress.</summary>
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);

    public const string SeedRequester = "system:first-start";

    public const string InterruptedMessage = "Interrupted: the application stopped or crashed while the service ran.";

    private readonly Dictionary<string, DateTimeOffset> _lookedAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _caughtUp = new(StringComparer.Ordinal);

    /// <summary>Runs until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StartUpAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var next = await TickAsync(cancellationToken);
                await Task.Delay(Slice(next - clock.GetUtcNow()), clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
        finally
        {
            await runner.StopAsync(StopWait);
        }
    }

    /// <summary>One wait: what is left, at most <see cref="MaxSlice"/>, never negative.</summary>
    internal static TimeSpan Slice(TimeSpan remaining) => TimeSpan.FromTicks(Math.Max(0, Math.Min(remaining.Ticks, MaxSlice.Ticks)));

    /// <summary>The start-up: recovery of interrupted runs, first-start settings, catch-up. Public so that tests can drive the clock.</summary>
    public async Task StartUpAsync(CancellationToken cancellationToken)
    {
        // read before the work below, which can be slow: a time that comes during it is seen by the first look, not lost
        var now = clock.GetUtcNow();
        _caughtUp.Clear();
        _lookedAt.Clear();
        await RecoverAsync(cancellationToken);
        await SeedAsync(cancellationToken);
        await ResumeRetriesAsync(cancellationToken);
        await CatchUpAsync(cancellationToken);
        foreach (var service in registry.All)
        {
            _lookedAt[service.Definition.Id] = now;
        }

        ServicesLog.SchedulerStarted(logger, registry.All.Count, now);
    }

    /// <summary>
    /// One look at the schedules: starts the services whose scheduled time came since the previous look (once each, however many times passed), starts the retries that are due and says when the next look
    /// should be. Public so that tests can drive the clock.
    /// </summary>
    public async Task<DateTimeOffset> TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var next = now + MaxSlice;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var all = await scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>().ReadAllAsync(cancellationToken);
            var latest = await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().GetLatestAsync(registry.Ids, cancellationToken);
            var calendar = await scope.ServiceProvider.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            foreach (var service in all)
            {
                var since = _lookedAt.TryGetValue(service.Id, out var seen) && seen <= now ? seen : now;
                _lookedAt[service.Id] = now;
                if (!service.Settings.Enabled)
                {
                    continue;
                }

                var schedule = service.Settings.Schedule;
                var handledAt = HandledAt(service, latest.GetValueOrDefault(service.Id));
                if (ScheduleCalculator.LatestAtOrBefore(schedule, now, calendar, service.NotBeforeSessionEnd) is { } due
                    && due > since
                    && (handledAt is null || handledAt < due))
                {
                    if (now - due <= LateAfter)
                    {
                        // a tick of an interval or window schedule that finds the service busy is dropped (log, no run record); a daily, weekly or run-after start is recorded as skipped
                        await StartAsync(service.Id, ServiceRunTrigger.Scheduled, cancellationToken, dropWhenBusy: schedule.Type is ScheduleType.Interval or ScheduleType.Window);
                    }
                    else if (service.Settings.CatchUp
                        && !_caughtUp.Contains(service.Id)
                        && ScheduleCalculator.MissedOccurrence(schedule, now, handledAt, calendar, service.NotBeforeSessionEnd, service.Definition.Constraints.CatchUpReach) is { } missed)
                    {
                        // the time passed unseen (the machine slept, the clock jumped): the same rules as after a restart, including at most one catch-up per service per start
                        _caughtUp.Add(service.Id);
                        ServicesLog.CatchUp(logger, service.Id, missed);
                        await StartAsync(service.Id, ServiceRunTrigger.CatchUp, cancellationToken);
                    }
                }

                if (ScheduleCalculator.NextAfter(schedule, now, calendar, service.NotBeforeSessionEnd) is { } upcoming && upcoming < next)
                {
                    next = upcoming;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed look must not end the scheduler; the next look is in a minute
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.TickCrashed(logger, ex);
        }

        await runner.StartDueRetriesAsync(cancellationToken);
        return runner.NextRetryAt is { } retry && retry < next ? retry : next;
    }

    /// <summary>
    /// Stores the first-start settings of the services that have no row yet (<c>Data:Eod:Enabled</c> and <c>Data:Eod:TimeIst</c>, the instrument refresh switch and time). Once a row exists it is the only source:
    /// the configuration is read here and never again, so an edit made on the Services screen survives a restart.
    /// </summary>
    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IServiceScheduleStore>();
            var existing = (await store.ListAsync(cancellationToken)).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var added = new List<(string Id, bool Enabled)>();
            foreach (var service in registry.All.Where(s => !existing.Contains(s.Definition.Id)))
            {
                if (service.FirstStartSettings() is { } settings)
                {
                    store.Add(ServiceSchedule.Create(service.Definition.Id, settings, SeedRequester, clock.GetUtcNow()));
                    added.Add((service.Definition.Id, settings.Enabled));
                }
            }

            if (added.Count > 0 && await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(cancellationToken))
            {
                foreach (var (id, enabled) in added)
                {
                    ServicesLog.Seeded(logger, id, enabled);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the services still run on their first-start settings read from the configuration; the next start tries again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.SeedFailed(logger, ex);
        }
    }

    /// <summary>
    /// The latest moment up to which the scheduled times are taken as dealt with: the start of the latest run (of any kind and outcome) and the last time the owner saved the service (pause, resume, edit), so a time that
    /// passed while it was paused is not made up for after a resume and a restart. The first-start settings are no decision of the owner and do not count.
    /// </summary>
    private static DateTimeOffset? HandledAt(EffectiveService service, ServiceRun? last)
    {
        DateTimeOffset? saved = service.Row is { } row && row.UpdatedBy != SeedRequester ? row.UpdatedAt : null;
        return (last?.StartedAt, saved) switch
        {
            (null, var s) => s,
            (var l, null) => l,
            var (l, s) => l > s ? l : s,
        };
    }

    /// <summary>
    /// A sequence of retries that the previous process left in its back-off goes on: the service's latest run is a failed attempt that may be followed by another (retries left, not a timeout, not interrupted, not long ago).
    /// The next attempt is due after the back-off, counted from the failure.
    /// </summary>
    private async Task ResumeRetriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var all = await scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>().ReadAllAsync(cancellationToken);
            var latest = await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().GetLatestAsync(registry.Ids, cancellationToken);
            var now = clock.GetUtcNow();
            foreach (var service in all.Where(s => s.Settings.Enabled && latest.TryGetValue(s.Id, out var run) && !ServiceRetryRules.IsOver(run, s.Settings.Retries, true, now)))
            {
                var failed = latest[service.Id];
                runner.ResumeRetry(service.Id, failed.Trigger, ServiceRunner.RetryRequester, failed.Attempt + 1, ServiceRetryRules.NextDue(failed));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // housekeeping: the scheduler must still run
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.TickCrashed(logger, ex);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().FailInterruptedAsync(application.StartedAt, clock.GetUtcNow(), InterruptedMessage, cancellationToken);
            if (count > 0)
            {
                ServicesLog.Recovered(logger, count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // housekeeping: the scheduler must still run (a stale record is also closed when its service is next started)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.RecoveryFailed(logger, ex);
        }
    }

    /// <summary>For every enabled service with catch-up on whose latest scheduled time passed without a run since (within the reach of the service), one run now.</summary>
    private async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var all = await scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>().ReadAllAsync(cancellationToken);
            var latest = await scope.ServiceProvider.GetRequiredService<IServiceRunStore>().GetLatestAsync(registry.Ids, cancellationToken);
            var calendar = await scope.ServiceProvider.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            var now = clock.GetUtcNow();
            foreach (var service in all.Where(s => s.Settings is { Enabled: true, CatchUp: true } && !_caughtUp.Contains(s.Id) && !runner.IsActive(s.Id)))
            {
                var lastStart = HandledAt(service, latest.GetValueOrDefault(service.Id));
                if (ScheduleCalculator.MissedOccurrence(service.Settings.Schedule, now, lastStart, calendar, service.NotBeforeSessionEnd, service.Definition.Constraints.CatchUpReach) is { } missed)
                {
                    _caughtUp.Add(service.Id);
                    ServicesLog.CatchUp(logger, service.Id, missed);
                    await StartAsync(service.Id, ServiceRunTrigger.CatchUp, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // if the catch-up cannot be decided, the schedule still runs; the next scheduled time makes up
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.TickCrashed(logger, ex);
        }
    }

    private async Task StartAsync(string serviceId, ServiceRunTrigger trigger, CancellationToken cancellationToken, bool dropWhenBusy = false)
    {
        try
        {
            _ = await runner.StartAsync(serviceId, trigger, ServiceRunner.SchedulerRequester, cancellationToken, dropWhenBusy);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one service that cannot start must not keep the others from starting
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ServicesLog.Crashed(logger, serviceId, ex);
        }
    }
}
