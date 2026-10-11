using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Refresh;

/// <summary>
/// Registers a refresh of the master list and queues it for the background worker. One refresh runs at a time (all instances): otherwise
/// <c>instruments.refresh_running</c>. <paramref name="Trigger"/> is <see cref="RefreshTrigger.Manual"/> or <see cref="RefreshTrigger.Daily"/>;
/// a manual request with <paramref name="Force"/> is recorded as <see cref="RefreshTrigger.Forced"/>.
/// </summary>
public sealed record StartRefreshCommand(RefreshTrigger Trigger, bool Force, string RequestedBy) : ICommand;

internal sealed class StartRefreshHandler(
    IRefreshRunStore runs,
    IRefreshJobChannel queue,
    TimeProvider clock,
    ILogger<StartRefreshHandler> logger) : ICommandHandler<StartRefreshCommand, RefreshStarted>
{
    public async ValueTask<Result<RefreshStarted>> HandleAsync(StartRefreshCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var force = command.Force && command.Trigger != RefreshTrigger.Daily;
        var trigger = force ? RefreshTrigger.Forced : command.Trigger;
        var run = RefreshRun.Start(trigger, force, command.RequestedBy, now);

        if (!await runs.TryStartAsync(run, RefreshLimits.StaleAfter, now, cancellationToken))
        {
            return Result.Failure<RefreshStarted>(MarketErrors.RefreshRunning);
        }

        MarketLog.RefreshStarted(logger, run.Id, trigger, force);
        queue.Enqueue(new RefreshJob(run.Id));
        return Result.Success(new RefreshStarted(run.Id));
    }
}
