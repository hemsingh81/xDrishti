using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// "Track my holdings" (ADR 0020 §3): tracks the instruments of the scope's open holdings that are in the instrument master, through the bulk tracking command (idempotent: what is tracked already is left alone, what the owner untracked is tracked
/// again). Holdings that are not in the master list are counted (<c>notInMaster</c>), instruments that are retired are skipped (<c>retired</c>). Starts no job: the next <i>Update data</i> builds the bars, and the answer says so. An account or bucket that does
/// not exist has no holdings, so nothing is tracked.
/// </summary>
public sealed record TrackPortfolioHoldingsCommand(Guid? AccountId, Guid? BucketId, string Actor) : ICommand;

internal sealed class TrackPortfolioHoldingsHandler(
    IHoldingQueries holdings,
    ITrackedInstrumentStore tracked,
    ICommandHandler<TrackInstrumentsCommand, TrackResult> track,
    ILogger<TrackPortfolioHoldingsHandler> logger) : ICommandHandler<TrackPortfolioHoldingsCommand, PortfolioTrackResult>
{
    public const string UpdateDataHint = "Nothing is fetched yet: run Update data (Data page) to build the price history of the newly tracked instruments.";

    public const string NothingHint = "Every holding in this scope that is in the instrument master is tracked already. Run Update data if its price history is missing.";

    public async ValueTask<Result<PortfolioTrackResult>> HandleAsync(TrackPortfolioHoldingsCommand command, CancellationToken cancellationToken)
    {
        var open = await holdings.ListAsync(new HoldingFilter(command.AccountId, command.BucketId, "open", null, TotalsOnly: command.AccountId is null), cancellationToken);
        var notInMaster = open.Count(h => h.InstrumentId is null);
        var ids = open.Where(h => h.InstrumentId is not null).Select(h => h.InstrumentId!.Value).Distinct().ToList();
        var listed = (await tracked.FindInstrumentsAsync(ids, cancellationToken)).Where(i => !i.Retired).Select(i => i.Id).ToList();
        var retired = ids.Count - listed.Count;

        int added = 0, already = 0, total = 0;
        foreach (var chunk in listed.Chunk(TrackingLimits.MaxIdsPerCall))
        {
            var result = await track.HandleAsync(new TrackInstrumentsCommand(new TrackingInput(chunk), command.Actor), cancellationToken);
            if (result.IsFailure)
            {
                return Result.Failure<PortfolioTrackResult>(result.Error);
            }

            added += result.Value.Added + result.Value.Reactivated;
            already += result.Value.AlreadyTracked;
            total = result.Value.TotalTracked;
        }

        if (listed.Count == 0)
        {
            total = await tracked.CountActiveAsync(cancellationToken);
        }

        PortfolioHistoryLog.HoldingsTracked(logger, listed.Count, added, already);
        return Result.Success(new PortfolioTrackResult(listed.Count, added, already, notInMaster, retired, total, added > 0 ? UpdateDataHint : NothingHint));
    }
}
