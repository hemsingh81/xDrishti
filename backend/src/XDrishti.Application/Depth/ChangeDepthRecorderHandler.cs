using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Depth;

/// <summary>
/// The owner's act of switching the depth recorder (P7-16). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update) and then one account event <c>DepthRecorderSwitched</c> in its own unit of
/// work (its failure is logged and does not undo the row - the rows are insert-only). The endpoint is responsible for the session and the fresh password. Starts nothing itself: the recorder follows the switch at its next look.
/// </summary>
internal sealed class ChangeDepthRecorderHandler(IDepthSwitchStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeDepthRecorderHandler> logger) : ICommandHandler<ChangeDepthRecorderCommand, DepthRecorderSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<DepthRecorderSwitched>> HandleAsync(ChangeDepthRecorderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            DepthLog.SwitchRefused(logger, "depth.owner_only");
            return Result.Failure<DepthRecorderSwitched>(Error.Forbidden("depth.owner_only", "Only the signed-in owner switches the depth recorder."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            DepthLog.SwitchRefused(logger, "depth.reason_invalid");
            return Result.Failure<DepthRecorderSwitched>(Error.Validation("depth.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        var row = await store.AppendAsync(command.Enabled, actor, reason, cancellationToken);
        DepthLog.SwitchChanged(logger, row.Id, row.Enabled);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.DepthRecorderSwitched, null, $"switch={row.Id} depth_recorder={(row.Enabled ? "on" : "off")}", actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // the row stands; the audit cross-index is a second write. Only the type of the failure is logged.
            DepthLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(new DepthRecorderSwitched(row.Id, row.Enabled));
    }
}

/// <summary>The answer of a switch change: the row and the new value.</summary>
public sealed record DepthRecorderSwitched(long Id, bool Enabled);
