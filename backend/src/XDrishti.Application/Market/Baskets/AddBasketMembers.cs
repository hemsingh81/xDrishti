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
/// Adds up to 500 instruments to a basket that is not archived, atomically and idempotently: the ones that are members already are skipped, the others are
/// appended in the order given, and the ones that are not tracked are tracked in the same transaction (<see cref="InstrumentTracking"/>). All or nothing:
/// an unknown instrument fails with <c>instruments.invalid</c>, a retired one that is not tracked with <c>instruments.retired</c>, and more than 2,000
/// members with <c>baskets.too_many_members</c>. Audited with counts.
/// </summary>
public sealed record AddBasketMembersCommand(Guid BasketId, AddMembersInput Input, string Actor) : ICommand;

internal sealed class AddBasketMembersHandler(
    IBasketStore store,
    IPortfolioBasketSource portfolio,
    InstrumentTracking tracking,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<AddBasketMembersHandler> logger) : ICommandHandler<AddBasketMembersCommand, AddMembersResult>
{
    public async ValueTask<Result<AddMembersResult>> HandleAsync(AddBasketMembersCommand command, CancellationToken cancellationToken)
    {
        var ids = BasketChecks.InstrumentIds(command.Input.InstrumentIds);
        if (ids.IsFailure)
        {
            return Result.Failure<AddMembersResult>(ids.Error);
        }

        var note = BasketMember.CheckNote(command.Input.Note);
        if (note.IsFailure)
        {
            return Result.Failure<AddMembersResult>(note.Error);
        }

        // A concurrent change of the basket (or the same instruments added at once) makes the save fail; reading again shows what the other request did.
        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var found = await BasketChecks.EditableAsync(store, portfolio, command.BasketId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<AddMembersResult>(found.Error);
            }

            var basket = found.Value;
            var members = await store.ListActiveMembersAsync(basket.Id, cancellationToken);
            var present = members.Select(m => m.InstrumentId).ToHashSet();
            var toAdd = ids.Value.Where(id => !present.Contains(id)).ToArray();
            var already = ids.Value.Length - toAdd.Length;
            if (toAdd.Length == 0)
            {
                return Result.Success(new AddMembersResult(ids.Value.Length, 0, already, 0, members.Count));
            }

            if (members.Count + toAdd.Length > BasketLimits.MaxActiveMembers)
            {
                return Result.Failure<AddMembersResult>(BasketErrors.TooManyMembers(members.Count, toAdd.Length));
            }

            var now = clock.GetUtcNow();
            var tracked = await tracking.TrackAsync(toAdd, command.Actor, now, BasketChecks.Called(basket), cancellationToken, relyOnTracked: true);
            if (tracked.IsFailure)
            {
                return Result.Failure<AddMembersResult>(tracked.Error);
            }

            var position = members.Count == 0 ? 0 : members.Max(m => m.Position) + 1;
            foreach (var id in toAdd)
            {
                store.Add(BasketMember.Add(basket.Id, id, position++, note.Value, command.Actor, now));
            }

            basket.Touch();
            events.Record(AccountEvent.Record(
                AccountEventType.BasketMembersAdded,
                null,
                $"Added {toAdd.Length} of {ids.Value.Length} instruments to {BasketChecks.Called(basket)} ({already} were members already, {tracked.Value.Changed} tracked now)",
                command.Actor,
                now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                MarketLog.BasketMembersAdded(logger, basket.Id, toAdd.Length, already, tracked.Value.Changed);
                return Result.Success(new AddMembersResult(ids.Value.Length, toAdd.Length, already, tracked.Value.Changed, members.Count + toAdd.Length));
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure<AddMembersResult>(BasketErrors.Conflict);
    }
}
