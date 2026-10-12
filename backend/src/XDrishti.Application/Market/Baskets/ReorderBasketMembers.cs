using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Puts the members of a basket that is not archived in the given order: the list must hold every current member exactly once
/// (<c>400 baskets.invalid</c> otherwise, or <c>409 baskets.conflict</c> when the members changed between two tries of this call). Positions become 0..n-1.
/// Idempotent: the same order again changes and audits nothing. The basket's revision moves, so a concurrent change is noticed.
/// </summary>
public sealed record ReorderBasketMembersCommand(Guid BasketId, MembersOrderInput Input, string Actor) : ICommand;

internal sealed class ReorderBasketMembersHandler(
    IBasketStore store,
    IPortfolioBasketSource portfolio,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<ReorderBasketMembersHandler> logger) : ICommandHandler<ReorderBasketMembersCommand>
{
    public async ValueTask<Result> HandleAsync(ReorderBasketMembersCommand command, CancellationToken cancellationToken)
    {
        var ids = BasketChecks.OrderedIds(command.Input.InstrumentIds, "instrumentIds", BasketLimits.MaxActiveMembers);
        if (ids.IsFailure)
        {
            return Result.Failure(ids.Error);
        }

        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var found = await BasketChecks.EditableAsync(store, portfolio, command.BasketId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure(found.Error);
            }

            var basket = found.Value;
            var members = (await store.ListActiveMembersAsync(basket.Id, cancellationToken)).ToDictionary(m => m.InstrumentId);
            if (members.Count != ids.Value.Length || ids.Value.Any(id => !members.ContainsKey(id)))
            {
                return Result.Failure(attempt == 0
                    ? BasketErrors.Invalid("instrumentIds", "The list must hold every instrument of the basket exactly once.")
                    : BasketErrors.Conflict);
            }

            var moved = 0;
            for (var position = 0; position < ids.Value.Length; position++)
            {
                if (members[ids.Value[position]].MoveTo(position))
                {
                    moved++;
                }
            }

            if (moved == 0)
            {
                return Result.Success();
            }

            basket.Touch();
            events.Record(AccountEvent.Record(
                AccountEventType.BasketMembersReordered,
                null,
                $"Reordered the {ids.Value.Length} instruments of {BasketChecks.Called(basket)} ({moved} moved)",
                command.Actor,
                clock.GetUtcNow()));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success();
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure(BasketErrors.Conflict);
    }
}
