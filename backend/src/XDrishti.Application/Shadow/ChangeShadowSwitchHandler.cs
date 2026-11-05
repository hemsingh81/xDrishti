using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The owner's act of switching the shadow record (spec CF-08 part B, D7, ADR 0212). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update) and then one account event <c>ShadowRecordSwitched</c> in its
/// own unit of work (its failure is logged and does not undo the row: the rows are insert-only). The endpoint is responsible for the session, the CSRF token and the fresh password. Starts nothing itself: the services follow the switch at their next run.
/// </summary>
internal sealed class ChangeShadowSwitchHandler(IShadowSwitchStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeShadowSwitchHandler> logger) : ICommandHandler<ChangeShadowSwitchCommand, ShadowSwitched>
{
    public const int MaxReasonLength = 500;

    public const string OwnerOnlyCode = "paper.shadow_owner_only";

    public const string ReasonInvalidCode = "paper.shadow_reason_invalid";

    public async ValueTask<Result<ShadowSwitched>> HandleAsync(ChangeShadowSwitchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            return Result.Failure<ShadowSwitched>(Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner switches the shadow record."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<ShadowSwitched>(Error.Validation(ReasonInvalidCode, "A change needs a reason of 1 to 500 characters."));
        }

        var id = await store.AppendAsync(command.Enabled, actor, reason, cancellationToken);
        var logged = (int)Math.Min(id, int.MaxValue);
        var on = command.Enabled ? 1 : 0;
        ShadowLog.SwitchChanged(logger, logged, on);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.ShadowRecordSwitched, null, $"switch={id} shadow_record={(command.Enabled ? "on" : "off")}", actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The row stands; the audit cross-index is a second write, and nothing of its failure is logged but that it happened.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            ShadowLog.AuditFailed(logger);
        }

        return Result.Success(new ShadowSwitched(id, command.Enabled));
    }
}
