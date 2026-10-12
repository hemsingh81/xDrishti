using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Tracking;

/// <summary>
/// Tracks up to 500 instruments in one atomic, idempotent call (see <see cref="InstrumentTracking.TrackAsync"/>); sets the time history collection
/// was asked for. Audited with the counts.
/// </summary>
public sealed record TrackInstrumentsCommand(TrackingInput Input, string Actor) : ICommand;

internal sealed class TrackInstrumentsHandler(
    InstrumentTracking tracking,
    ITrackedInstrumentStore store,
    IMarketUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<TrackInstrumentsHandler> logger) : ICommandHandler<TrackInstrumentsCommand, TrackResult>
{
    public async ValueTask<Result<TrackResult>> HandleAsync(TrackInstrumentsCommand command, CancellationToken cancellationToken)
    {
        var ids = TrackingIds.Check(command.Input);
        if (ids.IsFailure)
        {
            return Result.Failure<TrackResult>(ids.Error);
        }

        // A concurrent request that tracks the same new instrument wins the primary key; reading again finds its row and counts the instrument as already tracked.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var outcome = await tracking.TrackAsync(ids.Value, command.Actor, clock.GetUtcNow(), null, cancellationToken);
            if (outcome.IsFailure)
            {
                return Result.Failure<TrackResult>(outcome.Error);
            }

            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                var o = outcome.Value;
                MarketLog.InstrumentsTracked(logger, o.Added, o.Reactivated, o.AlreadyTracked);
                return Result.Success(new TrackResult(o.Requested, o.Added, o.Reactivated, o.AlreadyTracked, await store.CountActiveAsync(cancellationToken)));
            }
        }

        return Result.Failure<TrackResult>(MarketErrors.TrackingCollided);
    }
}
