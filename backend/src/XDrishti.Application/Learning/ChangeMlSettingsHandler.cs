using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The owner's act of switching the ML switches (spec CF-10 D6 to D8, part 10d-1, ADR 0220). Refuses a non-owner actor, a missing or over-long reason and a call that names no switch before anything is written; appends one row per switch that actually changes (never an update). The endpoint
/// is responsible for the session, the CSRF token and the fresh password. Turning <c>ml.influence.enabled</c> ON does not make any model influence anything: the predicate still needs the evidence terms. It starts nothing and promotes or demotes nothing itself.
/// </summary>
internal sealed class ChangeMlSettingsHandler(IMlSwitchSettings settings, IMlSwitchStore store, ILogger<ChangeMlSettingsHandler> logger) : ICommandHandler<ChangeMlSettingsCommand, MlSettingsChanged>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<MlSettingsChanged>> HandleAsync(ChangeMlSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!MlActors.IsOwner(command.Actor))
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.OwnerOnlyCode);
            return Result.Failure<MlSettingsChanged>(MlSafetyErrors.OwnerOnly());
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<MlSettingsChanged>(MlSafetyErrors.SettingsInvalid("A change needs a reason of 1 to 500 characters."));
        }

        if (command.Influence is null && command.AutoPromote is null && command.AutoRollback is null && command.AutoHoldOutScore is null)
        {
            return Result.Failure<MlSettingsChanged>(MlSafetyErrors.SettingsInvalid("A change names at least one of ml.influence.enabled, ml.auto_promote, ml.auto_rollback and ml.holdout.auto_score."));
        }

        var now = await settings.CurrentAsync(cancellationToken);
        var changed = new List<string>();
        foreach (var (name, wanted, current) in new[]
        {
            (MlSwitch.Influence, command.Influence, now.Influence.On),
            (MlSwitch.AutoPromote, command.AutoPromote, now.AutoPromote.On),
            (MlSwitch.AutoRollback, command.AutoRollback, now.AutoRollback.On),
            (MlSwitch.AutoHoldOutScore, command.AutoHoldOutScore, now.AutoHoldOutScore?.On ?? false),
        })
        {
            if (wanted is not { } value || value == current)
            {
                continue;
            }

            var id = await store.AppendAsync(name, value, command.Actor, reason, cancellationToken);
            MlSafetyLog.Switched(logger, id, name, value ? 1 : 0);
            changed.Add(name);
        }

        return Result.Success(new MlSettingsChanged(await settings.CurrentAsync(cancellationToken), changed));
    }
}
