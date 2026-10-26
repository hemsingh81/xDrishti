using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Calendar;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>Carries out a registered data job: the phases it asked for, in order. Runs in the background; the job record shows progress and result.</summary>
public sealed record ExecuteDataJobCommand(Guid JobId) : ICommand;

internal sealed class ExecuteDataJobHandler(
    IDataJobStore jobs,
    CsvPhaseRunner csv,
    CsvUndoRunner csvUndo,
    DhanPhaseRunner dhan,
    AggregatePhaseRunner aggregate,
    QualityPhaseRunner quality,
    ICalendarUpkeep calendar,
    IContextUpkeep context,
    TimeProvider clock,
    ILogger<ExecuteDataJobHandler> logger) : ICommandHandler<ExecuteDataJobCommand>
{
    private const string Stopping = "The update was interrupted because the application is stopping.";

    private const string Unexpected = "The update failed unexpectedly. See the API log.";

    public async ValueTask<Result> HandleAsync(ExecuteDataJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.JobId, cancellationToken);
        if (job is null || !job.IsRunning)
        {
            return Result.Success();
        }

        try
        {
            await RunAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DataLog.JobCancelled(logger, job.Id);
            await FailAsync(job, Stopping);
        }
#pragma warning disable CA1031 // any failure must end the job record as failed, never crash the worker
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.PhaseCrashed(logger, job.Id, job.CurrentPhase ?? "job", ex);
            await FailAsync(job, Unexpected);
        }

        return Result.Success();
    }

    private async ValueTask RunAsync(DataJob job, CancellationToken cancellationToken)
    {
        var reporter = new JobReporter(job, jobs, clock);
        foreach (var step in job.Steps)
        {
            try
            {
                switch (step)
                {
                    case DataSteps.Csv when job.UndoCsvFile is not null:
                        await reporter.ReportAsync(await csvUndo.RunAsync(job, reporter, cancellationToken), cancellationToken, publishNow: true);
                        break;
                    case DataSteps.Csv:
                        await reporter.ReportAsync(await csv.RunAsync(job, reporter, cancellationToken), cancellationToken, publishNow: true);
                        break;
                    case DataSteps.Dhan:
                        await reporter.ReportAsync(await dhan.RunAsync(reporter, cancellationToken), cancellationToken, publishNow: true);
                        break;
                    case DataSteps.Quality:
                        await reporter.ReportAsync(await quality.RunAsync(job, reporter, cancellationToken), cancellationToken, publishNow: true);
                        break;
                    default:
                        await reporter.ReportAsync(await aggregate.RunAsync(job, reporter, cancellationToken), cancellationToken, publishNow: true);
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // a phase crashing is recorded on the phase; the others still run (the CSV rows already stored are worth aggregating)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                DataLog.PhaseCrashed(logger, job.Id, step, ex);
                await reporter.ReportAsync(Crashed(job, step), cancellationToken, publishNow: true);
            }

            if (reporter.Ended)
            {
                DataLog.JobEndedElsewhere(logger, job.Id);
                return;
            }
        }

        // The stored bars may have shown new holidays or special sessions, and NSE's list is tried once a day (ADR 0017 §1); only ever warnings. A job about one instrument, or one
        // that only checks the data, brings in nothing the calendar could learn from.
        if (job.InstrumentId is null && job.Steps.Any(step => step != DataSteps.Quality))
        {
            await calendar.RunAsync(reporter, cancellationToken);
            if (reporter.Ended)
            {
                DataLog.JobEndedElsewhere(logger, job.Id);
                return;
            }
        }

        // The days that became final are written to the market context (P3-01d); only ever warnings.
        if (job.InstrumentId is null && job.Steps.Contains(DataSteps.Aggregate))
        {
            await context.RunAsync(reporter, cancellationToken);
            if (reporter.Ended)
            {
                DataLog.JobEndedElsewhere(logger, job.Id);
                return;
            }
        }

        job.Finish(clock.GetUtcNow());
        if (!await jobs.PublishAsync(job, CancellationToken.None))
        {
            DataLog.JobEndedElsewhere(logger, job.Id);
            return;
        }

        var status = job.Status.ToString().ToLowerInvariant();
        DataLog.JobFinished(logger, job.Id, status);
    }

    private IDataPhase Crashed(DataJob job, string step)
    {
        var now = clock.GetUtcNow();
        return step switch
        {
            DataSteps.Csv => job.Csv with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Unexpected },
            DataSteps.Dhan => job.Dhan with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Unexpected },
            DataSteps.Quality => job.Quality with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Unexpected },
            _ => job.Aggregate with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Unexpected },
        };
    }

    private async ValueTask FailAsync(DataJob job, string error)
    {
        if (!job.IsRunning)
        {
            return;
        }

        job.Fail(clock.GetUtcNow(), error);
        await jobs.PublishAsync(job, CancellationToken.None);
    }
}
