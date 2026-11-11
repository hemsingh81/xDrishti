using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Auth;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>
/// The owner's change of the basket settings (BK-08, criteria 10 and 11). Order: the actor is an owner; the fresh password for THIS change (401 / 423 from the step-up, the elevation window untouched); the values are checked against the settings in force and each key that changes
/// becomes one row with its direction; a basket named as the gate's source exists; then the rows are appended if nobody changed the settings meanwhile. Every change is the owner's, tightening ones included; no rule, job or model calls this. Nothing here places an order.
/// </summary>
internal sealed class ChangeBasketSettingsHandler(IBasketSettingsStore store, IBasketStore baskets, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeBasketSettingsHandler> logger) : ICommandHandler<ChangeBasketSettingsCommand, BasketSettingsView>
{
    public async ValueTask<Result<BasketSettingsView>> HandleAsync(ChangeBasketSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(Error.Forbidden(BasketSettingChanges.OwnerOnlyCode, "Only the signed-in owner changes the basket settings."));
        }

        var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
        if (checkedPassword.IsFailure)
        {
            return Refuse(checkedPassword.Error);
        }

        var state = await store.CurrentAsync(cancellationToken);
        var rows = BasketSettingChanges.For(command.Actor, state.Settings, command.BreadthSource, command.MaxClusterRiskPct, command.ClearCap, command.ClusterRho);
        if (rows.IsFailure)
        {
            return Refuse(rows.Error);
        }

        var sourceRow = rows.Value.FirstOrDefault(r => r.Key == BasketSettings.BreadthSourceKey);
        if (BasketSettings.BasketOf(sourceRow?.NewValue) is { } basketId && await baskets.FindAsync(basketId, cancellationToken) is null)
        {
            return Refuse(Error.NotFound("selection.basket_not_found", "That basket does not exist."));
        }

        if (!await store.AppendAsync(rows.Value, state.Version, cancellationToken))
        {
            return Refuse(Error.Conflict("selection.stale", "The settings were changed meanwhile; read them again."));
        }

        var view = await BasketSettingsViews.ReadAsync(store, cancellationToken);
        await BasketSettingsAudit.RecordAsync(events, unitOfWork, clock, logger, rows.Value, view.Version);
        var loosened = rows.Value.Count(r => r.Direction == BasketSettingChange.Loosen);
        BasketSettingsLog.Changed(logger, rows.Value.Count, loosened, view.Version);
        return Result.Success(view);
    }

    private Result<BasketSettingsView> Refuse(Error error)
    {
        BasketSettingsLog.Refused(logger, error.Code);
        return Result.Failure<BasketSettingsView>(error);
    }
}
