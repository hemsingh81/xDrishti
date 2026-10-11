using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Refresh;

/// <summary>
/// Run when the API's refresh worker starts: a refresh still marked running that began before this process did cannot be running (its process
/// ended), so it is marked failed instead of blocking new refreshes for 30 minutes. Assumes one API instance (v1).
/// </summary>
public sealed record RecoverInterruptedRefreshesCommand(DateTimeOffset ProcessStartedAt) : ICommand;

internal sealed class RecoverInterruptedRefreshesHandler(
    IRefreshRunStore runs,
    TimeProvider clock,
    ILogger<RecoverInterruptedRefreshesHandler> logger) : ICommandHandler<RecoverInterruptedRefreshesCommand>
{
    public async ValueTask<Result> HandleAsync(RecoverInterruptedRefreshesCommand command, CancellationToken cancellationToken)
    {
        var count = await runs.FailInterruptedAsync(command.ProcessStartedAt, clock.GetUtcNow(), cancellationToken);
        if (count > 0)
        {
            MarketLog.InterruptedRunsRecovered(logger, count);
        }

        return Result.Success();
    }
}
