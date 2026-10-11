using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Sets the acquisition date of one lot (between 1990-01-01 and today in India; not after the day a closed lot was closed). The lot stops using the
/// default date. Every figure is computed on read, so nothing else needs to be recalculated.
/// </summary>
public sealed record UpdateLotDateCommand(Guid HoldingId, Guid LotId, LotDateInput Input, string Actor) : ICommand;

internal sealed class UpdateLotDateHandler(
    IHoldingStore holdings,
    IHoldingQueries queries,
    IAccountStore accounts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<UpdateLotDateHandler> logger) : ICommandHandler<UpdateLotDateCommand, LotRow>
{
    public async ValueTask<Result<LotRow>> HandleAsync(UpdateLotDateCommand command, CancellationToken cancellationToken)
    {
        if (command.Input?.AcquiredOn is not { } date)
        {
            return Result.Failure<LotRow>(PortfolioErrors.Invalid("acquiredOn", "Required."));
        }

        var holding = await holdings.FindAsync(command.HoldingId, cancellationToken);
        var account = holding is null ? null : await accounts.FindReadOnlyAsync(holding.AccountId, cancellationToken);
        if (holding is null || account is null)
        {
            return Result.Failure<LotRow>(PortfolioErrors.HoldingNotFound);
        }

        var now = clock.GetUtcNow();
        var previous = holding.Lots.FirstOrDefault(l => l.Id == command.LotId)?.AcquiredOn;
        var changed = holding.ChangeLotDate(command.LotId, date, IndiaTime.Today(now));
        if (changed.IsFailure)
        {
            return Result.Failure<LotRow>(changed.Error);
        }

        events.Record(AccountEvent.Record(
            AccountEventType.LotDateChanged,
            holding.AccountId,
            $"Lot date of {holding.TradingSymbol} changed from {previous:yyyy-MM-dd} to {date:yyyy-MM-dd}",
            command.Actor,
            now));
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<LotRow>(PortfolioErrors.LotChanged);
        }

        PortfolioLog.LotDateChanged(logger, account.Slug);

        var fresh = await queries.GetAsync(command.HoldingId, cancellationToken);
        var lot = fresh?.Lots.FirstOrDefault(l => l.Id == command.LotId);
        return fresh is null || lot is null
            ? Result.Failure<LotRow>(PortfolioErrors.LotNotFound)
            : Result.Success(HoldingViews.Lot(fresh, lot, IndiaTime.Today(now)));
    }
}
