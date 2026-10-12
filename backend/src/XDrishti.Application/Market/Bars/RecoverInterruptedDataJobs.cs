using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// Run when the API's data worker starts: a job still marked running that began before this process did cannot be running (its process ended), so it is marked
/// failed instead of blocking new jobs. Assumes one API instance (v1).
/// </summary>
public sealed record RecoverInterruptedDataJobsCommand(DateTimeOffset ProcessStartedAt) : ICommand;

internal sealed class RecoverInterruptedDataJobsHandler(
    IDataJobStore jobs,
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

        return Result.Success();
    }
}
