using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The owner's record of a <c>verify-pack --gate</c> result (spec CF-11 row 15, ADR 0226). Refuses a non-owner actor, an unknown status, a malformed pack id or trade count, a missing or over-long reason and a strategy version that does not exist, before anything is written; appends one
/// row. The endpoint is responsible for the session, CSRF and the fresh password. It records a claim of the owner (the pack files are on the owner's disk); only a PASS over at least 20 trades counts toward the scorecard.
/// </summary>
internal sealed class RecordPackGateResultHandler(
    IPackGateResultStore store,
    IQueryHandler<GetStrategyQuery, Result<StrategyDetail>> strategyDetail,
    ILogger<RecordPackGateResultHandler> logger) : ICommandHandler<RecordPackGateResultCommand, PackGateRecorded>
{
    public const int MaxPackIdLength = 100;

    public async ValueTask<Result<PackGateRecorded>> HandleAsync(RecordPackGateResultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!KillActors.IsOwner(command.Actor))
        {
            ReadinessLog.Refused(logger, KillErrors.OwnerOnlyCode);
            return Result.Failure<PackGateRecorded>(KillErrors.OwnerOnly());
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ChangeReadinessSwitchHandler.MaxReasonLength)
        {
            return Result.Failure<PackGateRecorded>(Error.Validation(KillErrors.ReasonInvalidCode, "A record needs a reason of 1 to 500 characters."));
        }

        var packId = command.PackId?.Trim();
        if (string.IsNullOrEmpty(packId) || packId.Length > MaxPackIdLength)
        {
            return Result.Failure<PackGateRecorded>(ReadinessErrors.Invalid("packId", "The pack id is the one the gate printed, 1 to 100 characters."));
        }

        if (!PackGateStatuses.IsKnown(command.Status))
        {
            return Result.Failure<PackGateRecorded>(ReadinessErrors.Invalid("status", "The status is one the gate prints: " + string.Join(", ", PackGateStatuses.All) + "."));
        }

        if (command.TradeCount < 0)
        {
            return Result.Failure<PackGateRecorded>(ReadinessErrors.Invalid("tradeCount", "The trade count cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(command.StrategyId) || command.Version < 1)
        {
            return Result.Failure<PackGateRecorded>(ReadinessErrors.Invalid("version", "A strategy id and a version of 1 or more are required."));
        }

        var detail = await strategyDetail.HandleAsync(new GetStrategyQuery(command.StrategyId), cancellationToken);
        if (detail.IsFailure)
        {
            return Result.Failure<PackGateRecorded>(ReadinessErrors.ScorecardNotFound(command.StrategyId, null));
        }

        if (!detail.Value.Versions.Any(v => v.Version == command.Version))
        {
            return Result.Failure<PackGateRecorded>(ReadinessErrors.ScorecardNotFound(command.StrategyId, command.Version));
        }

        var id = await store.AppendAsync(command.StrategyId, command.Version, packId, command.Status, command.TradeCount, command.Actor, reason, cancellationToken);
        ReadinessLog.PackGateRecorded(logger, id, command.Status, command.TradeCount);
        return Result.Success(new PackGateRecorded(id, command.Status, PackGateStatuses.Counts(command.Status, command.TradeCount)));
    }
}
