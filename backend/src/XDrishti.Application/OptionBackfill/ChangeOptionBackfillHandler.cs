using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// The owner's act of switching the expired-options backfill (P14-03 criterion 6, as P14-02's). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update) and then one account event
/// <c>OptionBackfillSwitched</c> in its own unit of work (its failure is logged and does not undo the row). The endpoint is responsible for the session and the fresh password. Starts nothing itself.
/// </summary>
internal sealed class ChangeOptionBackfillHandler(IOptionBackfillSwitchStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeOptionBackfillHandler> logger) : ICommandHandler<ChangeOptionBackfillCommand, OptionBackfillSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<OptionBackfillSwitched>> HandleAsync(ChangeOptionBackfillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OptionBackfillOwner.IsOwner(command.Actor))
        {
            OptionBackfillLog.SwitchRefused(logger, "options.owner_only");
            return Result.Failure<OptionBackfillSwitched>(Error.Forbidden("options.owner_only", "Only the signed-in owner switches the expired-options backfill."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            OptionBackfillLog.SwitchRefused(logger, "options.reason_invalid");
            return Result.Failure<OptionBackfillSwitched>(Error.Validation("options.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        var row = await store.AppendAsync(command.Enabled, command.Actor, reason, cancellationToken);
        OptionBackfillLog.SwitchChanged(logger, row.Id, row.Enabled);
        await OptionBackfillOwner.RecordAsync(events, unitOfWork, logger, AccountEventType.OptionBackfillSwitched, $"switch={row.Id} option_backfill={(row.Enabled ? "on" : "off")}", command.Actor, clock.GetUtcNow());
        return Result.Success(new OptionBackfillSwitched(row.Id, row.Enabled));
    }
}

/// <summary>The owner's acceptance of the probe result (P14-03 criterion 9): one insert-only row and one account event. Same guards as the switch.</summary>
internal sealed class AcceptBackfillProbeHandler(IBackfillProbeAckStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<AcceptBackfillProbeHandler> logger) : ICommandHandler<AcceptBackfillProbeCommand, BackfillProbeAccepted>
{
    public async ValueTask<Result<BackfillProbeAccepted>> HandleAsync(AcceptBackfillProbeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OptionBackfillOwner.IsOwner(command.Actor))
        {
            OptionBackfillLog.SwitchRefused(logger, "options.owner_only");
            return Result.Failure<BackfillProbeAccepted>(Error.Forbidden("options.owner_only", "Only the signed-in owner accepts the probe result."));
        }

        var note = command.Note?.Trim();
        if (string.IsNullOrEmpty(note) || note.Length > ChangeOptionBackfillHandler.MaxReasonLength)
        {
            OptionBackfillLog.SwitchRefused(logger, "options.reason_invalid");
            return Result.Failure<BackfillProbeAccepted>(Error.Validation("options.reason_invalid", "The acceptance needs a note of 1 to 500 characters."));
        }

        var row = await store.AppendAsync(command.Actor, note, cancellationToken);
        OptionBackfillLog.ProbeAccepted(logger, row.Id);
        await OptionBackfillOwner.RecordAsync(events, unitOfWork, logger, AccountEventType.OptionBackfillProbeAccepted, $"probe_ack={row.Id}", command.Actor, clock.GetUtcNow());
        return Result.Success(new BackfillProbeAccepted(row.Id));
    }
}

internal static class OptionBackfillOwner
{
    public static bool IsOwner(string? actor) => actor is not null && actor.StartsWith("owner:", StringComparison.Ordinal) && actor.Length > "owner:".Length && actor.Length <= 100;

    public static async ValueTask RecordAsync(IAccountEventLog events, IAccountUnitOfWork unitOfWork, ILogger logger, AccountEventType type, string detail, string actor, DateTimeOffset now)
    {
        try
        {
            events.Record(AccountEvent.Record(type, null, detail, actor, now));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // the row stands; the audit cross-index is a second write. Only the type of the failure is logged.
            OptionBackfillLog.AuditFailed(logger, failure.GetType().Name);
        }
    }
}
