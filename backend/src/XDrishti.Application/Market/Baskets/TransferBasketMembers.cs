using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Copies or moves up to 500 instruments from one basket to another (both not archived, different), atomically and idempotently, in one transaction:
/// the members of the source that are not in the target are appended to it (in the source's order, with their notes) and tracked when they are not
/// tracked; a move also ends their stay in the source. Instruments that are not members of the source are skipped (counted as <c>notInSource</c>), so
/// repeating a move changes nothing. Failures are as for adding (<c>instruments.retired</c>, <c>baskets.too_many_members</c>). Audited with counts.
/// </summary>
public sealed record TransferBasketMembersCommand(TransferInput Input, string Actor) : ICommand;

internal sealed class TransferBasketMembersHandler(
    IBasketStore store,
    InstrumentTracking tracking,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<TransferBasketMembersHandler> logger) : ICommandHandler<TransferBasketMembersCommand, TransferResult>
{
    public const string Copy = "copy";

    public const string Move = "move";

    public async ValueTask<Result<TransferResult>> HandleAsync(TransferBasketMembersCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var mode = input.Mode?.Trim().ToLower(CultureInfo.InvariantCulture);
        if (mode is not (Copy or Move))
        {
            return Result.Failure<TransferResult>(BasketErrors.Invalid("mode", "Use copy or move."));
        }

        if (input.FromBasketId == Guid.Empty || input.ToBasketId == Guid.Empty)
        {
            return Result.Failure<TransferResult>(BasketErrors.Invalid(input.FromBasketId == Guid.Empty ? "fromBasketId" : "toBasketId", "Pick a basket."));
        }

        if (input.FromBasketId == input.ToBasketId)
        {
            return Result.Failure<TransferResult>(BasketErrors.Invalid("toBasketId", "Pick a different basket than the source."));
        }

        var ids = BasketChecks.InstrumentIds(input.InstrumentIds);
        if (ids.IsFailure)
        {
            return Result.Failure<TransferResult>(ids.Error);
        }

        var moving = mode == Move;
        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var source = await BasketChecks.EditableAsync(store, input.FromBasketId, cancellationToken);
            if (source.IsFailure)
            {
                return Result.Failure<TransferResult>(source.Error);
            }

            var target = await BasketChecks.EditableAsync(store, input.ToBasketId, cancellationToken);
            if (target.IsFailure)
            {
                return Result.Failure<TransferResult>(target.Error);
            }

            var sourceMembers = await store.ListActiveMembersAsync(source.Value.Id, cancellationToken);
            var targetMembers = await store.ListActiveMembersAsync(target.Value.Id, cancellationToken);
            var wanted = ids.Value.ToHashSet();
            var transferable = sourceMembers.Where(m => wanted.Contains(m.InstrumentId)).ToList(); // in the source's order
            var inTarget = targetMembers.Select(m => m.InstrumentId).ToHashSet();
            var toAdd = transferable.Where(m => !inTarget.Contains(m.InstrumentId)).ToList();
            var alreadyInTarget = transferable.Count - toAdd.Count;
            var notInSource = ids.Value.Length - transferable.Count;
            var removed = moving ? transferable.Count : 0;

            var newlyTracked = 0;
            var now = clock.GetUtcNow();
            if (toAdd.Count > 0)
            {
                if (targetMembers.Count + toAdd.Count > BasketLimits.MaxActiveMembers)
                {
                    return Result.Failure<TransferResult>(BasketErrors.TooManyMembers(targetMembers.Count, toAdd.Count));
                }

                var tracked = await tracking.TrackAsync([.. toAdd.Select(m => m.InstrumentId)], command.Actor, now, BasketChecks.Called(target.Value), cancellationToken, relyOnTracked: true);
                if (tracked.IsFailure)
                {
                    return Result.Failure<TransferResult>(tracked.Error);
                }

                newlyTracked = tracked.Value.Changed;
                var position = targetMembers.Count == 0 ? 0 : targetMembers.Max(m => m.Position) + 1;
                foreach (var member in toAdd)
                {
                    store.Add(BasketMember.Add(target.Value.Id, member.InstrumentId, position++, member.Note, command.Actor, now));
                }

                target.Value.Touch();
            }

            if (moving && transferable.Count > 0)
            {
                foreach (var member in transferable)
                {
                    member.Remove(command.Actor, now);
                }

                source.Value.Touch();
            }

            var result = new TransferResult(
                mode, ids.Value.Length, toAdd.Count, alreadyInTarget, removed, notInSource, newlyTracked,
                sourceMembers.Count - removed, targetMembers.Count + toAdd.Count);
            if (toAdd.Count == 0 && removed == 0)
            {
                return Result.Success(result);
            }

            var done = moving ? removed : toAdd.Count;
            events.Record(AccountEvent.Record(
                AccountEventType.BasketMembersTransferred,
                null,
                $"{(moving ? "Moved" : "Copied")} {done} of {ids.Value.Length} instruments from {BasketChecks.Called(source.Value)} to {BasketChecks.Called(target.Value)} "
                    + $"({alreadyInTarget} already there, {notInSource} not in the source, {newlyTracked} tracked now)",
                command.Actor,
                now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                MarketLog.BasketMembersTransferred(logger, source.Value.Id, target.Value.Id, mode, toAdd.Count, alreadyInTarget, removed, notInSource);
                return Result.Success(result);
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure<TransferResult>(BasketErrors.Conflict);
    }
}
