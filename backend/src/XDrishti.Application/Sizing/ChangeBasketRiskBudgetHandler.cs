using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Auth;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>
/// The owner's change of one basket's risk budget (BK-10, criterion 9): the actor is an owner, the fresh password for THIS change, the basket exists, the budget is 1 to 100 (or null to remove it), then one row with its direction is appended if nobody changed
/// that basket's budget meanwhile. Raising or removing a budget is a loosening and is recorded so. Nothing here places an order.
/// </summary>
internal sealed class ChangeBasketRiskBudgetHandler(ISizingStore store, IBasketStore baskets, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ILogger<ChangeBasketRiskBudgetHandler> logger) : ICommandHandler<ChangeBasketRiskBudgetCommand, BasketRiskBudgetsView>
{
    public async ValueTask<Result<BasketRiskBudgetsView>> HandleAsync(ChangeBasketRiskBudgetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(Error.Forbidden(SizingChanges.OwnerOnlyCode, "Only the signed-in owner changes a basket risk budget."));
        }

        var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
        if (checkedPassword.IsFailure)
        {
            return Refuse(checkedPassword.Error);
        }

        if (await baskets.FindAsync(command.BasketId, cancellationToken) is null)
        {
            return Refuse(Error.NotFound("selection.basket_not_found", "That basket does not exist."));
        }

        var current = await store.BudgetsAsync(cancellationToken);
        double? now = current.TryGetValue(command.BasketId, out var pct) ? pct : null;
        var change = SizingChanges.ForBudget(command.Actor, command.BasketId, now, command.BudgetPct);
        if (change.IsFailure)
        {
            return Refuse(change.Error);
        }

        var next = command.BudgetPct is { } b ? Math.Round(b, 2) : (double?)null;
        if (!await store.AppendBudgetAsync(command.BasketId, now, next, change.Value, cancellationToken))
        {
            return Refuse(Error.Conflict("selection.stale", "The budget was changed meanwhile; read it again."));
        }

        SizingLog.BudgetChanged(logger, change.Value.Direction);
        return Result.Success(await SizingViews.BudgetsAsync(store, cancellationToken));
    }

    private Result<BasketRiskBudgetsView> Refuse(Error error)
    {
        SizingLog.Refused(logger, error.Code);
        return Result.Failure<BasketRiskBudgetsView>(error);
    }
}
