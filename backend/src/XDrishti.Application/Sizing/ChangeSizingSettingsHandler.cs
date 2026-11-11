using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Auth;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>
/// The owner's change of the sizing settings (BK-10, criterion 9). Order: the actor is an owner; the fresh password for THIS change (401 / 423 from the step-up, the elevation window untouched); then the values are checked against the settings in force and each key that
/// changes becomes one row with its direction; then the rows are appended if nobody changed the settings meanwhile. Every change is the owner's, loosening ones included; no rule, job or model calls this. Nothing here places an order.
/// </summary>
internal sealed class ChangeSizingSettingsHandler(ISizingStore store, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, CostStressOptions costStress, ILogger<ChangeSizingSettingsHandler> logger) : ICommandHandler<ChangeSizingSettingsCommand, SizingSettingsView>
{
    public async ValueTask<Result<SizingSettingsView>> HandleAsync(ChangeSizingSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Refuse(Error.Forbidden(SizingChanges.OwnerOnlyCode, "Only the signed-in owner changes the sizing settings."));
        }

        var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
        if (checkedPassword.IsFailure)
        {
            return Refuse(checkedPassword.Error);
        }

        var state = await store.CurrentAsync(cancellationToken);
        var rows = SizingChanges.ForSettings(command.Actor, state.Settings, command.VolTargetEnabled, command.Floor, command.CapacityEnabled, command.CapacityPct, command.MinFit, (double)costStress.CapacityDailyShare);
        if (rows.IsFailure)
        {
            return Refuse(rows.Error);
        }

        if (!await store.AppendSettingsAsync(rows.Value, state.Version, cancellationToken))
        {
            return Refuse(Error.Conflict("selection.stale", "The settings were changed meanwhile; read them again."));
        }

        var view = await SizingViews.SettingsAsync(store, cancellationToken);
        await SizingAudit.RecordAsync(events, unitOfWork, clock, logger, rows.Value, view.Version);
        var loosened = rows.Value.Count(r => r.Direction == SizingChange.Loosen);
        SizingLog.SettingsChanged(logger, rows.Value.Count, loosened, view.Version);
        return Result.Success(view);
    }

    private Result<SizingSettingsView> Refuse(Error error)
    {
        SizingLog.Refused(logger, error.Code);
        return Result.Failure<SizingSettingsView>(error);
    }
}
