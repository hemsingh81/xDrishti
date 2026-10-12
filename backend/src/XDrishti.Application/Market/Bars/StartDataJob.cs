using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// Registers a data job (CSV files → Dhan fetch → aggregates, or any of them) and queues it for the background worker. One job runs at a time (all instances):
/// otherwise <c>data.job_running</c>. A job with the CSV phase needs the instrument list (<c>data.master_missing</c>). <c>steps</c> unknown or empty is <c>data.invalid</c>.
/// </summary>
public sealed record StartDataJobCommand(StartDataJobRequest Request, string RequestedBy) : ICommand;

internal sealed class StartDataJobHandler(
    IDataJobStore jobs,
    IDataJobChannel queue,
    IInstrumentCatalog catalog,
    TimeProvider clock,
    ILogger<StartDataJobHandler> logger) : ICommandHandler<StartDataJobCommand, DataJobView>
{
    public async ValueTask<Result<DataJobView>> HandleAsync(StartDataJobCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var steps = request.Steps ?? DataSteps.All;
        if (steps.Count == 0)
        {
            return Result.Failure<DataJobView>(DataErrors.Invalid("steps", "Choose at least one of csv, dhan and aggregate."));
        }

        var unknown = steps.Where(s => !DataSteps.IsKnown(s)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure<DataJobView>(DataErrors.Invalid("steps", "Use csv, dhan and aggregate."));
        }

        if (steps.Contains(DataSteps.Csv) && await catalog.CountActiveAsync(cancellationToken) == 0)
        {
            return Result.Failure<DataJobView>(DataErrors.MasterMissing);
        }

        var now = clock.GetUtcNow();
        var job = DataJob.Start(steps, request.ForceCsv ?? false, command.RequestedBy, now);
        if (!await jobs.TryStartAsync(job, DataLimits.StaleAfter, now, cancellationToken))
        {
            return Result.Failure<DataJobView>(DataErrors.JobRunning);
        }

        var stepList = string.Join(',', job.Steps);
        DataLog.JobStarted(logger, job.Id, stepList, job.ForceCsv);
        queue.Enqueue(new DataJobTicket(job.Id));
        return Result.Success(DataJobView.From(job));
    }
}
