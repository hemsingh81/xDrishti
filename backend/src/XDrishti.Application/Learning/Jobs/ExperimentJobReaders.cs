using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>One job, read only.</summary>
internal sealed class GetExperimentJobHandler(IExperimentJobStore jobs, IRunStore runs) : IQueryHandler<GetExperimentJobQuery, Result<ExperimentJobView>>
{
    public async ValueTask<Result<ExperimentJobView>> HandleAsync(GetExperimentJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var job = await jobs.GetAsync(query.Id, cancellationToken);
        if (job is null)
        {
            return Result.Failure<ExperimentJobView>(ExperimentJobErrors.NotFound(query.Id));
        }

        return Result.Success(ExperimentJobViews.Of(job, job.ReplayMs is null ? null : await runs.FindAsync(job.BaselineRunKey, cancellationToken)));
    }
}

/// <summary>The newest jobs, read only (limit 1 to 200).</summary>
internal sealed class ListExperimentJobsHandler(IExperimentJobStore jobs) : IQueryHandler<ListExperimentJobsQuery, Result<ExperimentJobList>>
{
    public async ValueTask<Result<ExperimentJobList>> HandleAsync(ListExperimentJobsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<ExperimentJobList>(LearningErrors.Invalid([new FieldError("limit", "The limit is 1 to 200.")]));
        }

        return Result.Success(new ExperimentJobList([.. (await jobs.ListAsync(query.Limit, cancellationToken)).Select(j => ExperimentJobViews.Of(j, null))]));
    }
}

/// <summary>
/// Stops a job (spec P8-18 criterion 8): a queued job is cancelled at once, a running one is asked to stop at its next safe point between instruments (nothing is stored, no ledger row, not a trial); a final job is refused. The safe direction: it needs the session and the CSRF
/// token, not the password and not the capability.
/// </summary>
internal sealed class CancelExperimentJobHandler(IExperimentJobStore jobs, IExperimentJobProgress progress, IRunStore runs, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<CancelExperimentJobHandler> logger)
    : ICommandHandler<CancelExperimentJobCommand, ExperimentJobView>
{
    public async ValueTask<Result<ExperimentJobView>> HandleAsync(CancelExperimentJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        var outcome = await jobs.RequestCancelAsync(command.Id, now, cancellationToken);
        var outcomeName = outcome.ToString();
        ExperimentJobLogEvents.CancelAsked(logger, command.Id, outcomeName);
        if (outcome == ExperimentJobCancelOutcome.NotFound)
        {
            return Result.Failure<ExperimentJobView>(ExperimentJobErrors.NotFound(command.Id));
        }

        if (outcome == ExperimentJobCancelOutcome.NotCancellable)
        {
            return Result.Failure<ExperimentJobView>(ExperimentJobErrors.NotCancellable);
        }

        events.Record(AccountEvent.Record(AccountEventType.ExperimentJobCancelled, null, "state=" + (outcome == ExperimentJobCancelOutcome.Cancelled ? "queued" : "running"), command.Actor, now));
        await unitOfWork.TrySaveChangesAsync(cancellationToken);
        var job = (await jobs.GetAsync(command.Id, cancellationToken))!;
        if (outcome == ExperimentJobCancelOutcome.Cancelled)
        {
            await progress.FinishedAsync(new ExperimentJobFinished(job.Id, ExperimentJobStates.Code(job.State)), cancellationToken);
        }

        return Result.Success(ExperimentJobViews.Of(job, await runs.FindAsync(job.BaselineRunKey, cancellationToken)));
    }
}
