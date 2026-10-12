using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Removes up to 500 instruments from a basket that is not archived, atomically and idempotently: members get an end date (the stay stays in the history),
/// the others are counted as <c>notMembers</c>. The instruments stay tracked, whichever baskets they leave. Audited with counts when something changed.
/// </summary>
public sealed record RemoveBasketMembersCommand(Guid BasketId, RemoveMembersInput Input, string Actor) : ICommand;

internal sealed class RemoveBasketMembersHandler(
    IBasketStore store,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<RemoveBasketMembersHandler> logger) : ICommandHandler<RemoveBasketMembersCommand, RemoveMembersResult>
{
    public async ValueTask<Result<RemoveMembersResult>> HandleAsync(RemoveBasketMembersCommand command, CancellationToken cancellationToken)
    {
        var ids = BasketChecks.InstrumentIds(command.Input.InstrumentIds);
        if (ids.IsFailure)
        {
            return Result.Failure<RemoveMembersResult>(ids.Error);
        }

        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var found = await BasketChecks.EditableAsync(store, command.BasketId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<RemoveMembersResult>(found.Error);
            }

            var basket = found.Value;
            var members = await store.ListActiveMembersAsync(basket.Id, cancellationToken);
            var wanted = ids.Value.ToHashSet();
            var leaving = members.Where(m => wanted.Contains(m.InstrumentId)).ToList();
            var notMembers = ids.Value.Length - leaving.Count;
            if (leaving.Count == 0)
            {
                return Result.Success(new RemoveMembersResult(ids.Value.Length, 0, notMembers, members.Count));
            }

            var now = clock.GetUtcNow();
            foreach (var member in leaving)
            {
                member.Remove(command.Actor, now);
            }

            basket.Touch();
            events.Record(AccountEvent.Record(
                AccountEventType.BasketMembersRemoved,
                null,
                $"Removed {leaving.Count} of {ids.Value.Length} instruments from {BasketChecks.Called(basket)} ({notMembers} were not members)",
                command.Actor,
                now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                MarketLog.BasketMembersRemoved(logger, basket.Id, leaving.Count, notMembers);
                return Result.Success(new RemoveMembersResult(ids.Value.Length, leaving.Count, notMembers, members.Count - leaving.Count));
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure<RemoveMembersResult>(BasketErrors.Conflict);
    }
}
