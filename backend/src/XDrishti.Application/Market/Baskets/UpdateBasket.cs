using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Replaces the name, colour, purpose and description of a basket that is not archived (the members are not touched). Idempotent: nothing differs, nothing is
/// saved or audited. A new name must be free among the active baskets (ignoring case). Audited: a rename and any other change are separate entries.
/// </summary>
public sealed record UpdateBasketCommand(Guid Id, BasketInput Input, string Actor) : ICommand;

internal sealed class UpdateBasketHandler(
    IBasketStore store,
    IBasketQueries queries,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<UpdateBasketHandler> logger) : ICommandHandler<UpdateBasketCommand, BasketView>
{
    public async ValueTask<Result<BasketView>> HandleAsync(UpdateBasketCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var details = Basket.Check(input.Name, input.Colour, input.Purpose, input.Description);
        if (details.IsFailure)
        {
            return Result.Failure<BasketView>(details.Error);
        }

        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var found = await BasketChecks.EditableAsync(store, command.Id, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<BasketView>(found.Error);
            }

            var basket = found.Value;
            if (BasketChecks.NameTaken(await store.ListActiveAsync(cancellationToken), details.Value.Name, basket.Id))
            {
                return Result.Failure<BasketView>(BasketErrors.NameTaken(details.Value.Name));
            }

            var (oldName, oldColour, oldPurpose, oldDescription) = (basket.Name, basket.Colour, basket.Purpose, basket.Description);
            if (!basket.Update(details.Value))
            {
                return Result.Success((await queries.GetAsync(basket.Id, cancellationToken))!);
            }

            var now = clock.GetUtcNow();
            if (oldName != basket.Name)
            {
                events.Record(AccountEvent.Record(AccountEventType.BasketRenamed, null, $"Renamed basket '{oldName}' to '{basket.Name}'", command.Actor, now));
            }

            var changed = new List<string>(3);
            if (oldColour != basket.Colour)
            {
                changed.Add("colour");
            }

            if (oldPurpose != basket.Purpose)
            {
                changed.Add("purpose");
            }

            if (oldDescription != basket.Description)
            {
                changed.Add("description");
            }

            if (changed.Count > 0)
            {
                events.Record(AccountEvent.Record(AccountEventType.BasketUpdated, null, $"Changed the {string.Join(", ", changed)} of {BasketChecks.Called(basket)}", command.Actor, now));
            }

            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success((await queries.GetAsync(command.Id, cancellationToken))!);
            }

            // Refused: when another request took the name, say so now (the database compares names with lower(), which can differ from the check above).
            if (oldName != details.Value.Name && await store.NameTakenAsync(details.Value.Name, command.Id, cancellationToken))
            {
                return Result.Failure<BasketView>(BasketErrors.NameTaken(details.Value.Name));
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure<BasketView>(BasketErrors.Conflict);
    }
}
