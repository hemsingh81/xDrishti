using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>What one look at the schedule did.</summary>
public enum EodOutcome
{
    /// <summary>Today is not a trading day, or its run time has not come yet.</summary>
    NotDue,

    /// <summary>An automatic run exists for today already (done, skipped or still running): never a second one.</summary>
    AlreadyDone,

    /// <summary>The data job was started.</summary>
    Started,

    /// <summary>Not started, and recorded as a skipped run with the reason (another job was running, or the Data account cannot fetch).</summary>
    Skipped,

    /// <summary>Something unexpected went wrong (a database error); nothing was started.</summary>
    Crashed,
}

/// <summary>
/// The scheduled end-of-day run (ADR 0017 §2, P1-12a), the shape of <c>DailyRefreshRunner</c>: waits for the next run time of <see cref="DataEodSchedule"/> in slices of at most a minute
/// that read the clock again, then starts the data job (<see cref="DataEodSteps"/>: <c>dhan</c>, <c>aggregate</c>, then the data quality checks; trigger <c>daily</c>) through the same command as the endpoint. When it starts on a trading day
/// after the run time and no automatic run exists for the day, it runs at once (the application was down). An automatic run happens at most once per day; when another job is
/// running or the primary Data account cannot fetch it is recorded as a skipped run with the reason instead (unless a run of the day appeared meanwhile). The start-up catch-up first waits for
/// <see cref="DataJobRecovery"/>, so a job left "running" by the previous process does not count as running. The API's hosted service only calls <see cref="RunAsync"/>.
/// </summary>
public sealed class DataEodRunner(IServiceScopeFactory scopes, IDataSettings settings, DataJobRecovery recovery, TimeProvider clock, ILogger<DataEodRunner> logger)
{
    /// <summary>Recorded as the requester of automatic runs.</summary>
    public const string Requester = "system:data-eod";

    /// <summary>The longest single wait; the clock is read again after each.</summary>
    public static readonly TimeSpan MaxSlice = TimeSpan.FromMinutes(1);

    /// <summary>The longest the start-up catch-up waits for the cleanup of interrupted jobs.</summary>
    public static readonly TimeSpan RecoveryWait = TimeSpan.FromMinutes(2);

    private const int StartAttempts = 3;

    /// <summary>Runs until cancelled, or returns at once when the end-of-day run is switched off.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!settings.EodEnabled)
        {
            DataLog.EodOff(logger);
            return;
        }

        try
        {
            await WaitForRecoveryAsync(cancellationToken);
            if (await RunDueAsync(cancellationToken) is EodOutcome.Started or EodOutcome.Skipped)
            {
                DataLog.EodCatchUp(logger);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var next = await NextRunAsync(cancellationToken);
                if (next is null)
                {
                    await Task.Delay(MaxSlice, clock, cancellationToken);
                    continue;
                }

                DataLog.EodScheduled(logger, next.Value);
                while (true)
                {
                    // the clock is read once per slice and the slice never goes below zero: a clock that jumped past the run time must not make the delay negative
                    var remaining = next.Value - clock.GetUtcNow();
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    await Task.Delay(Slice(remaining), clock, cancellationToken);
                }

                for (var attempt = 1; attempt <= StartAttempts; attempt++)
                {
                    if (await RunDueAsync(cancellationToken) != EodOutcome.Crashed)
                    {
                        break;
                    }

                    await Task.Delay(MaxSlice, clock, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>One wait: what is left, at most <see cref="MaxSlice"/>, never negative.</summary>
    internal static TimeSpan Slice(TimeSpan remaining) => TimeSpan.FromTicks(Math.Max(0, Math.Min(remaining.Ticks, MaxSlice.Ticks)));

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

    /// <summary>
    /// Starts today's automatic run when it is due (a trading day, run time passed) and none exists for the day. Public for the tests of the decision; the loop calls it at
    /// the run time and once at start-up (the catch-up).
    /// </summary>
    public async Task<EodOutcome> RunDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var now = clock.GetUtcNow();
            var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            if (!DataEodSchedule.IsDue(now, settings.EodTimeIst, calendar))
            {
                return EodOutcome.NotDue;
            }

            var jobs = services.GetRequiredService<IDataJobStore>();
            if (await jobs.GetLastByTriggerAsync(DataJobTrigger.Daily, cancellationToken) is { } last && IndiaTime.Today(last.StartedAt) == IndiaTime.Today(now))
            {
                return EodOutcome.AlreadyDone;
            }

            var steps = DataEodSteps.Planned();
            var unavailable = await WhyNotAsync(services, cancellationToken);
            if (unavailable is not null)
            {
                return await SkipAsync(jobs, steps, unavailable, now, cancellationToken);
            }

            var start = services.GetRequiredService<ICommandHandler<StartDataJobCommand, DataJobView>>();
            var result = await start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(steps), Requester, DataJobTrigger.Daily), cancellationToken);
            if (result.IsSuccess)
            {
                DataLog.EodStarted(logger, result.Value.Id);
                return EodOutcome.Started;
            }

            // Another run of the day may have appeared between the check above and the start (a second instance): then there is nothing to record.
            if (await jobs.GetLastByTriggerAsync(DataJobTrigger.Daily, cancellationToken) is { } appeared && IndiaTime.Today(appeared.StartedAt) == IndiaTime.Today(now))
            {
                return EodOutcome.AlreadyDone;
            }

            return result.Error.Code == DataErrors.JobRunningCode
                ? await SkipAsync(jobs, steps, "Another data update was running.", now, cancellationToken)
                : await SkipAsync(jobs, steps, result.Error.Message, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed start must not end the schedule; tomorrow's run is still planned
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.EodCrashed(logger, ex);
            return EodOutcome.Crashed;
        }
    }

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

    private async ValueTask<EodOutcome> SkipAsync(IDataJobStore jobs, IReadOnlyList<string> steps, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await jobs.RecordSkippedAsync(DataJob.Skipped(steps, DataJobTrigger.Daily, Requester, reason, now), cancellationToken);
        DataLog.EodSkipped(logger, reason);
        return EodOutcome.Skipped;
    }

    /// <summary>The next run by the current calendar; null when it cannot be read (logged) or the calendar has no trading day ahead.</summary>
    private async ValueTask<DateTimeOffset?> NextRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var calendar = await scope.ServiceProvider.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            return DataEodSchedule.NextRun(clock.GetUtcNow(), settings.EodTimeIst, calendar);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the calendar could not be read: try again in a minute
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.EodCrashed(logger, ex);
            return null;
        }
    }
}
