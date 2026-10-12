using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// Archives a basket: it disappears from the list, its members and history stay readable, and it cannot be changed any more. Idempotent: an archived
/// basket stays as it is and nothing is audited. Instruments are not untracked. Audited.
/// </summary>
public sealed record ArchiveBasketCommand(Guid Id, string Actor) : ICommand;

internal sealed class ArchiveBasketHandler(
    IBasketStore store,
    IPortfolioBasketSource portfolio,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<ArchiveBasketHandler> logger) : ICommandHandler<ArchiveBasketCommand>
{
    public async ValueTask<Result> HandleAsync(ArchiveBasketCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < BasketChecks.Attempts; attempt++)
        {
            var basket = await store.FindAsync(command.Id, cancellationToken);
            if (basket is null)
            {
                return Result.Failure(await portfolio.ExistsAsync(command.Id, cancellationToken) ? BasketErrors.System : BasketErrors.NotFound);
            }

            var now = clock.GetUtcNow();
            var members = (await store.ListActiveMembersAsync(basket.Id, cancellationToken)).Count;
            if (!basket.Archive(now))
            {
                return Result.Success();
            }

            events.Record(AccountEvent.Record(AccountEventType.BasketArchived, null, $"Archived {BasketChecks.Called(basket)} ({members} instruments)", command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                MarketLog.BasketArchived(logger, basket.Id);
                return Result.Success();
            }
        }

        MarketLog.BasketConflict(logger, BasketChecks.Attempts);
        return Result.Failure(BasketErrors.Conflict);
    }
}
