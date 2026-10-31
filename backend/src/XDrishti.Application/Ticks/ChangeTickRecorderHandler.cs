using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ticks;

/// <summary>
/// The owner's act of switching the trade-tick recorder (P7-16b criterion 6, as P7-16's). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update) and then one account event
/// <c>TickRecorderSwitched</c> in its own unit of work (its failure is logged and does not undo the row). The endpoint is responsible for the session and the fresh password. Starts nothing itself: the recorder follows the switch at its next look.
/// </summary>
internal sealed class ChangeTickRecorderHandler(ITickSwitchStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeTickRecorderHandler> logger) : ICommandHandler<ChangeTickRecorderCommand, TickRecorderSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<TickRecorderSwitched>> HandleAsync(ChangeTickRecorderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            TickLog.SwitchRefused(logger, "ticks.owner_only");
            return Result.Failure<TickRecorderSwitched>(Error.Forbidden("ticks.owner_only", "Only the signed-in owner switches the trade-tick recorder."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            TickLog.SwitchRefused(logger, "ticks.reason_invalid");
            return Result.Failure<TickRecorderSwitched>(Error.Validation("ticks.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        var row = await store.AppendAsync(command.Enabled, actor, reason, cancellationToken);
        TickLog.SwitchChanged(logger, row.Id, row.Enabled);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.TickRecorderSwitched, null, $"switch={row.Id} tick_recorder={(row.Enabled ? "on" : "off")}", actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // the row stands; the audit cross-index is a second write. Only the type of the failure is logged.
            TickLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(new TickRecorderSwitched(row.Id, row.Enabled));
    }
}
