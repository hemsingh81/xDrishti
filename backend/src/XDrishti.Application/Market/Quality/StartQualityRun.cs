using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Quality;

/// <summary>The body of "Run checks": the one instrument to check (null: every tracked instrument with bars).</summary>
public sealed record StartQualityRunRequest(Guid? InstrumentId = null);

/// <summary>
/// Starts the data quality checks in the background as a data job with the single step <c>quality</c> (so it shares the one-job-at-a-time rule, the worker, the progress and the recovery of every data job):
/// <c>data.job_running</c> while any data job or quality run is in progress, <c>data.instrument_not_found</c> for an unknown instrument. The result is the job; its <c>quality.runId</c> names the run.
/// </summary>
public sealed record StartQualityRunCommand(StartQualityRunRequest Request, string RequestedBy) : ICommand;

internal sealed class StartQualityRunHandler(
    IBarQueries queries,
    ICommandHandler<StartDataJobCommand, DataJobView> start) : ICommandHandler<StartQualityRunCommand, DataJobView>
{
    public async ValueTask<Result<DataJobView>> HandleAsync(StartQualityRunCommand command, CancellationToken cancellationToken)
    {
        var instrumentId = command.Request.InstrumentId;
        if (instrumentId is { } id && await queries.FindInstrumentAsync(id, cancellationToken) is null)
        {
            return Result.Failure<DataJobView>(DataErrors.InstrumentNotFound);
        }

        return await start.HandleAsync(
            new StartDataJobCommand(new StartDataJobRequest([DataSteps.Quality]), command.RequestedBy, DataJobTrigger.Manual, instrumentId),
            cancellationToken);
    }
}
