using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Run when the API starts: a holdings sync still marked running that began before this process did cannot be running (its process ended), so it is
/// marked failed instead of blocking the account for ten minutes; so is a broker history run (trades or ledger, ADR 0025). Assumes one API instance (v1).
/// </summary>
public sealed record RecoverInterruptedSyncsCommand(DateTimeOffset ProcessStartedAt) : ICommand;

internal sealed class RecoverInterruptedSyncsHandler(
    IHoldingSyncStore syncs,
    IBrokerSyncStore brokerRuns,
    TimeProvider clock,
    ILogger<RecoverInterruptedSyncsHandler> logger) : ICommandHandler<RecoverInterruptedSyncsCommand>
{
    public async ValueTask<Result> HandleAsync(RecoverInterruptedSyncsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var count = await syncs.FailInterruptedAsync(command.ProcessStartedAt, now, cancellationToken)
            + await brokerRuns.FailInterruptedAsync(command.ProcessStartedAt, now, cancellationToken);
        if (count > 0)
        {
            PortfolioLog.InterruptedRunsRecovered(logger, count);
        }

        return Result.Success();
    }
}
