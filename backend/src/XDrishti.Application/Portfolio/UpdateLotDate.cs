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
/// default date. Every figure is computed on read, so nothing else needs to be recalculated. A lot rebuilt from trades takes its date from the trade
/// (<c>409 portfolio.lot_from_trade</c>), and the opening lot of such a holding must stay before the earliest trade (<c>400 portfolio.invalid</c>, field <c>acquiredOn</c>); see <see cref="Holding.ChangeLotDate"/>.
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

        // The holding's row is marked as changed (Touch) although only a lot is: saving then checks its concurrency token, so an automatic rebuild after a broker sync (ADR 0025) that read the holding before this edit cannot save over the date the owner
        // just set (it is skipped and tried again), and an edit made on a holding that a sync, a quote or a rebuild changed meanwhile is repeated once on what is stored now (RunAsync) instead of overwriting it.
        string? slug = null;
        var saved = await unitOfWork.RunAsync(
            async token =>
            {
                var holding = await holdings.FindAsync(command.HoldingId, token);
                var account = holding is null ? null : await accounts.FindReadOnlyAsync(holding.AccountId, token);
                if (holding is null || account is null)
                {
                    return Result.Failure(PortfolioErrors.HoldingNotFound);
                }

                var now = clock.GetUtcNow();
                var previous = holding.Lots.FirstOrDefault(l => l.Id == command.LotId)?.AcquiredOn;
                var changed = holding.ChangeLotDate(command.LotId, date, IndiaTime.Today(now));
                if (changed.IsFailure)
                {
                    return changed;
                }

                holdings.Touch(holding);
                events.Record(AccountEvent.Record(
                    AccountEventType.LotDateChanged,
                    holding.AccountId,
                    $"Lot date of {holding.TradingSymbol} changed from {previous:yyyy-MM-dd} to {date:yyyy-MM-dd}",
                    command.Actor,
                    now));
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    return Result.Failure(PortfolioErrors.LotChanged);
                }

                slug = account.Slug;
                return Result.Success();
            },
            cancellationToken);
        if (saved.IsFailure)
        {
            // A second concurrency conflict (the run repeated once already) is the same conflict to the owner as a refused save.
            return Result.Failure<LotRow>(saved.Error == PortfolioErrors.DataChanged ? PortfolioErrors.LotChanged : saved.Error);
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        PortfolioLog.LotDateChanged(logger, slug!);

        var fresh = await queries.GetAsync(command.HoldingId, cancellationToken);
        var lot = fresh?.Lots.FirstOrDefault(l => l.Id == command.LotId);
        return fresh is null || lot is null
            ? Result.Failure<LotRow>(PortfolioErrors.LotNotFound)
            : Result.Success(HoldingViews.Lot(fresh, lot, today));
    }
}
