using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Run when the API starts: a holdings sync still marked running that began before this process did cannot be running (its process ended), so it is
/// marked failed instead of blocking the account for ten minutes. Assumes one API instance (v1).
/// </summary>
public sealed record RecoverInterruptedSyncsCommand(DateTimeOffset ProcessStartedAt) : ICommand;

internal sealed class RecoverInterruptedSyncsHandler(
    IHoldingSyncStore syncs,
    TimeProvider clock,
    ILogger<RecoverInterruptedSyncsHandler> logger) : ICommandHandler<RecoverInterruptedSyncsCommand>
{
    public async ValueTask<Result> HandleAsync(RecoverInterruptedSyncsCommand command, CancellationToken cancellationToken)
    {
        var count = await syncs.FailInterruptedAsync(command.ProcessStartedAt, clock.GetUtcNow(), cancellationToken);
        if (count > 0)
        {
            PortfolioLog.InterruptedRunsRecovered(logger, count);
        }

        return Result.Success();
    }
}
