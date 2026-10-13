using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// The end-of-day data run as a service (<see cref="Id"/>, ADR 0017 §2, ADR 0018): one run starts the data job of <see cref="DataEodSteps"/> (<c>dhan</c>, <c>aggregate</c>, then the data quality checks;
/// trigger <c>daily</c>) through the same command as the endpoint and waits for it, so the run ends as the job does (its progress is linked to the run record). <b>When</b> it runs is the scheduler's: by default every
/// trading day at 15:50, never before that day's session has ended. A scheduled run happens at most once per day: when an automatic job exists for today already it is skipped; when another job is running or
/// the primary Data account cannot fetch it is recorded as a skipped run with the reason (and as a skipped job, which keeps the day's run from being tried again). A manual run (Run now) has none of these day rules.
/// A run waits for <see cref="DataJobRecovery"/> first, so a job left "running" by the previous process is not mistaken for a running one.
/// </summary>
public sealed class EodService(IServiceScopeFactory scopes, IDataSettings settings, DataJobRecovery recovery, TimeProvider clock, ILogger<EodService> logger) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "eod";

    /// <summary>Recorded as the requester of automatic runs.</summary>
    public const string Requester = "system:data-eod";

    public const string OwnJobRunning = "The previous end-of-day update is still running.";

    public const string AnotherJobRunning = "Another data update was running.";

    public const string AlreadyRanToday = "An automatic end-of-day update exists for today already.";

    /// <summary>The longest the first run waits for the cleanup of interrupted jobs.</summary>
    public static readonly TimeSpan RecoveryWait = TimeSpan.FromMinutes(2);

    public static readonly ServiceDefinition Definition = new(
        Id,
        "End-of-day data run",
        "Fetches the day's 1-minute bars from Dhan, builds every timeframe and checks the data quality. Never starts before the session has ended.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(15, 50)), 0, 240, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartNotBefore = new TimeOnly(15, 35),
            StartAdvice = "The session ends at 15:30; the last minutes may not be available from Dhan yet.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary><c>Data:Eod:Enabled</c> and <c>Data:Eod:TimeIst</c>: read once, when the service has no stored row (ADR 0018 §3).</summary>
    public ServiceSettings? FirstStartSettings() => Definition.Defaults with { Enabled = settings.EodEnabled, Schedule = Schedule.Daily(settings.EodTimeIst) };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await WaitForRecoveryAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var jobs = services.GetRequiredService<IDataJobStore>();
        var now = clock.GetUtcNow();
        var automatic = context.Trigger != ServiceRunTrigger.Manual;
        var dayRules = automatic && context.Attempt == 1;
        if (dayRules && await RanTodayAsync(jobs, now, cancellationToken))
        {
            return ServiceOutcome.Skipped(AlreadyRanToday);
        }

        var steps = DataEodSteps.Planned();
        if (await WhyNotAsync(services, cancellationToken) is { } unavailable)
        {
            return await SkipAsync(jobs, steps, unavailable, automatic, now, cancellationToken);
        }

        var start = services.GetRequiredService<ICommandHandler<StartDataJobCommand, DataJobView>>();
        var started = await start.HandleAsync(
            new StartDataJobCommand(new StartDataJobRequest(steps), automatic ? Requester : context.RequestedBy, automatic ? DataJobTrigger.Daily : DataJobTrigger.Manual),
            cancellationToken);
        if (started.IsFailure)
        {
            // Another run of the day may have appeared between the check above and the start (a second instance): then there is nothing to record.
            if (dayRules && await RanTodayAsync(jobs, now, cancellationToken))
            {
                return ServiceOutcome.Skipped(AlreadyRanToday);
            }

            if (started.Error.Code != DataErrors.JobRunningCode)
            {
                return await SkipAsync(jobs, steps, started.Error.Message, automatic, now, cancellationToken);
            }

            // A job of the end-of-day run itself that is still going (an earlier run timed out while it went on) is not "another" update, and is no reason to write a skipped job that would hide it.
            var own = await jobs.GetRunningAsync(cancellationToken) is { Trigger: DataJobTrigger.Daily };
            return await SkipAsync(jobs, steps, own ? OwnJobRunning : AnotherJobRunning, automatic && !own, now, cancellationToken);
        }

        DataLog.EodStarted(logger, started.Value.Id);
        await context.LinkDataJobAsync(started.Value.Id, cancellationToken);
        return await WaitForJobAsync(jobs, started.Value.Id, cancellationToken);
    }

    private async ValueTask<ServiceOutcome> WaitForJobAsync(IDataJobStore jobs, Guid jobId, CancellationToken cancellationToken)
    {
        var wait = JobPolling.FirstWait;
        while (true)
        {
            var job = await jobs.GetAsync(jobId, cancellationToken);
            if (job is null)
            {
                return ServiceOutcome.Failed("The data update record is gone.");
            }

            if (!job.IsRunning)
            {
                return job.Status == DataJobStatus.Succeeded ? ServiceOutcome.Succeeded() : ServiceOutcome.Failed(job.Error ?? "The data update failed.");
            }

            await Task.Delay(wait, clock, cancellationToken);
            wait = JobPolling.Next(wait);
        }
    }

    private async Task WaitForRecoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await recovery.Completed.WaitAsync(RecoveryWait, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            DataLog.EodRecoveryTimedOut(logger);
        }
    }

    private static async ValueTask<bool> RanTodayAsync(IDataJobStore jobs, DateTimeOffset now, CancellationToken cancellationToken) =>
        await jobs.GetLastByTriggerAsync(DataJobTrigger.Daily, cancellationToken) is { } last && IndiaTime.Today(last.StartedAt) == IndiaTime.Today(now);

    /// <summary>Why a run would be pointless right now (the reason in words), or null when it can go ahead.</summary>
    private static async ValueTask<string?> WhyNotAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var availability = await services.GetRequiredService<IIntradayBarSource>().CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return availability.Reason ?? "The Dhan Data API fetch is not available.";
        }

        var account = await services.GetRequiredService<IDataAccountStatus>().GetAsync(cancellationToken);
        return account.CanFetch ? null : account.Reason ?? "There is no connected primary Data account.";
    }

    private async ValueTask<ServiceOutcome> SkipAsync(IDataJobStore jobs, IReadOnlyList<string> steps, string reason, bool recordJob, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (recordJob)
        {
            // the skipped job is what keeps the day's automatic run from being tried again (and what the Data screen shows as the day's run)
            await jobs.RecordSkippedAsync(DataJob.Skipped(steps, DataJobTrigger.Daily, Requester, reason, now), cancellationToken);
        }

        DataLog.EodSkipped(logger, reason);
        return ServiceOutcome.Skipped(reason);
    }
}
