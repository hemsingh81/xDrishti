using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>Creates an empty basket at the end of the list. The name must be free among the active baskets (ignoring case). Audited.</summary>
public sealed record CreateBasketCommand(BasketInput Input, string Actor) : ICommand;

internal sealed class CreateBasketHandler(
    IBasketStore store,
    IBasketQueries queries,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<CreateBasketHandler> logger) : ICommandHandler<CreateBasketCommand, BasketView>
{
    public async ValueTask<Result<BasketView>> HandleAsync(CreateBasketCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var details = Basket.Check(input.Name, input.Colour, input.Purpose, input.Description);
        if (details.IsFailure)
        {
            return Result.Failure<BasketView>(details.Error);
        }

        // Two requests with the same name at once: the database lets one of them in; the other reads again and is told that the name is taken.
        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var active = await store.ListActiveAsync(cancellationToken);
            if (BasketChecks.NameTaken(active, details.Value.Name))
            {
                return Result.Failure<BasketView>(BasketErrors.NameTaken(details.Value.Name));
            }

            var now = clock.GetUtcNow();
            var basket = Basket.Create(details.Value, active.Count == 0 ? 0 : active.Max(b => b.Position) + 1, now);
            store.Add(basket);
            events.Record(AccountEvent.Record(AccountEventType.BasketCreated, null, $"Created {BasketChecks.Called(basket)} ({basket.Purpose.ToCode()})", command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                MarketLog.BasketCreated(logger, basket.Id);
                return Result.Success((await queries.GetAsync(basket.Id, cancellationToken))!);
            }

            // Refused: when another request took the name, say so now (the database compares names with lower(), which can differ from the check above).
            if (await store.NameTakenAsync(details.Value.Name, null, cancellationToken))
            {
                return Result.Failure<BasketView>(BasketErrors.NameTaken(details.Value.Name));
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure<BasketView>(BasketErrors.Conflict);
    }
}
