using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Puts the active baskets in the given order: the list must hold every active basket exactly once (a basket created or archived meanwhile makes it
/// <c>400 baskets.invalid</c>, or <c>409 baskets.conflict</c> when that happened between two tries of this call). Positions become 0..n-1.
/// Idempotent: the same order again changes and audits nothing.
/// </summary>
public sealed record ReorderBasketsCommand(BasketOrderInput Input, string Actor) : ICommand;

internal sealed class ReorderBasketsHandler(
    IBasketStore store,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<ReorderBasketsHandler> logger) : ICommandHandler<ReorderBasketsCommand>
{
    public async ValueTask<Result> HandleAsync(ReorderBasketsCommand command, CancellationToken cancellationToken)
    {
        var ids = BasketChecks.OrderedIds(command.Input.Ids, "ids", int.MaxValue);
        if (ids.IsFailure)
        {
            return Result.Failure(ids.Error);
        }

        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var active = (await store.ListActiveAsync(cancellationToken)).ToDictionary(b => b.Id);
            if (active.Count != ids.Value.Length || ids.Value.Any(id => !active.ContainsKey(id)))
            {
                return Result.Failure(attempt == 0
                    ? BasketErrors.Invalid("ids", "The list must hold every basket exactly once.")
                    : BasketErrors.Conflict);
            }

            var moved = 0;
            for (var position = 0; position < ids.Value.Length; position++)
            {
                if (active[ids.Value[position]].MoveTo(position))
                {
                    moved++;
                }
            }

            if (moved == 0)
            {
                return Result.Success();
            }

            events.Record(AccountEvent.Record(AccountEventType.BasketsReordered, null, $"Reordered {ids.Value.Length} baskets ({moved} moved)", command.Actor, clock.GetUtcNow()));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success();
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure(BasketErrors.Conflict);
    }
}
