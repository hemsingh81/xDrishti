using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Autonomy;

/// <summary>
/// The owner's act of changing the autonomy settings (spec P7-12). The domain refuses a non-owner actor and out-of-range values before anything is written; the store appends one row (never an update) if nobody changed the settings
/// meanwhile. The caller (the endpoint) is responsible for the authenticated identity and the password re-entry.
/// </summary>
internal sealed class ChangeAutonomySettingsHandler(IAutonomySettingsStore store, ILogger<ChangeAutonomySettingsHandler> logger) : ICommandHandler<ChangeAutonomySettingsCommand, AutonomySettingsView>
{
    public async ValueTask<Result<AutonomySettingsView>> HandleAsync(ChangeAutonomySettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var change = AutonomyChange.Create(command.Actor, command.Reason, command.AutoApproveTickets, command.PromoteFromReserve, command.MinPWin, command.MaxAutoPerDay);
        if (change.IsFailure)
        {
            AutonomyLog.Refused(logger, change.Error.Code);
            return Result.Failure<AutonomySettingsView>(change.Error);
        }

        var current = await store.CurrentAsync(cancellationToken);
        if (command.ExpectedSettingsId is { } expected && expected != (current.Id ?? 0))
        {
            // The form was loaded at another row (P8-19): nothing is written; the owner reads the settings again.
            AutonomyLog.Refused(logger, "autonomy.stale");
            return Result.Failure<AutonomySettingsView>(Error.Conflict("autonomy.stale", "The settings were changed meanwhile; read them again."));
        }

        var row = await store.AppendAsync(change.Value, current.Id, cancellationToken);
        if (row is null)
        {
            AutonomyLog.Refused(logger, "autonomy.stale");
            return Result.Failure<AutonomySettingsView>(Error.Conflict("autonomy.stale", "The settings were changed meanwhile; read them again."));
        }

        AutonomyLog.SettingsChanged(logger, row.Id, row.AutoApproveTickets, row.PromoteFromReserve);
        var updated = new AutonomySettings(row.Id, row.Hash, row.AutoApproveTickets, row.PromoteFromReserve, row.MinPWin, row.MaxAutoPerDay);
        return Result.Success(GetAutonomySettingsHandler.View(updated, await store.HistoryAsync(GetAutonomySettingsHandler.HistoryLimit, cancellationToken)));
    }
}
