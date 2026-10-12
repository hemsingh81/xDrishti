using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Tracking;

/// <summary>
/// Untracks up to 500 instruments in one atomic, idempotent call (see <see cref="InstrumentTracking.UntrackAsync"/>). The history of an
/// untracked instrument is kept; tracking it again continues from it. Audited with the counts.
/// </summary>
public sealed record UntrackInstrumentsCommand(TrackingInput Input, string Actor) : ICommand;

internal sealed class UntrackInstrumentsHandler(
    InstrumentTracking tracking,
    ITrackedInstrumentStore store,
    IMarketUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UntrackInstrumentsHandler> logger) : ICommandHandler<UntrackInstrumentsCommand, UntrackResult>
{
    public async ValueTask<Result<UntrackResult>> HandleAsync(UntrackInstrumentsCommand command, CancellationToken cancellationToken)
    {
        var ids = TrackingIds.Check(command.Input);
        if (ids.IsFailure)
        {
            return Result.Failure<UntrackResult>(ids.Error);
        }

        // A concurrent request that changed one of the rows (tracked it again, or added it to a basket) refuses the save; reading again shows what it did.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var outcome = await tracking.UntrackAsync(ids.Value, command.Actor, clock.GetUtcNow(), null, cancellationToken);
            if (outcome.IsFailure)
            {
                return Result.Failure<UntrackResult>(outcome.Error);
            }

            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                var o = outcome.Value;
                MarketLog.InstrumentsUntracked(logger, o.Untracked, o.NotTracked);
                return Result.Success(new UntrackResult(o.Requested, o.Untracked, o.NotTracked, await store.CountActiveAsync(cancellationToken)));
            }
        }

        return Result.Failure<UntrackResult>(MarketErrors.TrackingCollided);
    }
}
