using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Alerts;

/// <summary>
/// Cancels an active price alert. Idempotent: a cancelled alert stays cancelled and a triggered one stays triggered (the answer shows its real status); nothing is deleted. The change is conditional on the alert still being active, so it cannot undo a trigger
/// that happened meanwhile. Audited when it cancels. <c>portfolio.not_found</c> for an unknown id.
/// </summary>
public sealed record CancelPriceAlertCommand(Guid Id, string Actor) : ICommand;

internal sealed class CancelPriceAlertHandler(
    IPriceAlertStore alerts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<CancelPriceAlertHandler> logger) : ICommandHandler<CancelPriceAlertCommand, PriceAlertView>
{
    public async ValueTask<Result<PriceAlertView>> HandleAsync(CancelPriceAlertCommand command, CancellationToken cancellationToken)
    {
        var cancelled = false;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                cancelled = false;
                if (await alerts.FindAsync(command.Id, token) is not { } alert)
                {
                    return Result.Failure(PortfolioErrors.AlertNotFound);
                }

                if (alert.Status != AlertStatus.Active || !await alerts.TryCancelAsync(command.Id, token))
                {
                    return Result.Success();
                }

                events.Record(AccountEvent.Record(
                    AccountEventType.PriceAlertCancelled,
                    null,
                    $"Price alert cancelled: {alert.Symbol} {AlertText.Condition(alert.Direction)} {AlertText.Price(alert.Level)}",
                    command.Actor,
                    clock.GetUtcNow()));
                await unitOfWork.SaveAsync(token);
                cancelled = true;
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<PriceAlertView>(outcome.Error);
        }

        if (cancelled)
        {
            AlertsLog.Cancelled(logger, command.Id);
        }

        var row = await alerts.FindAsync(command.Id, cancellationToken);
        return row is null ? Result.Failure<PriceAlertView>(PortfolioErrors.AlertNotFound) : Result.Success(PriceAlertViews.Of(row));
    }
}
