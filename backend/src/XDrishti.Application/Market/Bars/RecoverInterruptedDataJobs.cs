using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// Run when the API's data worker starts: a job (and a quality run) still marked running that began before this process did cannot be running (its process ended), so it is marked
/// failed instead of blocking new jobs. Assumes one API instance (v1).
/// </summary>
public sealed record RecoverInterruptedDataJobsCommand(DateTimeOffset ProcessStartedAt) : ICommand;

internal sealed class RecoverInterruptedDataJobsHandler(
    IDataJobStore jobs,
    IQualityRuns qualityRuns,
    TimeProvider clock,
    ILogger<RecoverInterruptedDataJobsHandler> logger) : ICommandHandler<RecoverInterruptedDataJobsCommand>
{
    public async ValueTask<Result> HandleAsync(RecoverInterruptedDataJobsCommand command, CancellationToken cancellationToken)
    {
        var count = await jobs.FailInterruptedAsync(command.ProcessStartedAt, clock.GetUtcNow(), cancellationToken);
        if (count > 0)
        {
            DataLog.InterruptedJobsRecovered(logger, count);
        }

        var runs = await qualityRuns.FailInterruptedAsync(command.ProcessStartedAt, clock.GetUtcNow(), cancellationToken);
        if (runs > 0)
        {
            QualityLog.InterruptedRunsRecovered(logger, runs);
        }

        return Result.Success();
    }
}
