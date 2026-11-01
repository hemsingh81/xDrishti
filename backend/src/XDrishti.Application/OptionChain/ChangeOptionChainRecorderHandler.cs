using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionChain;

/// <summary>
/// The owner's act of switching the option-chain recorder (P14-02 criterion 7, as P7-16's). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update) and then one account event
/// <c>OptionChainRecorderSwitched</c> in its own unit of work (its failure is logged and does not undo the row). The endpoint is responsible for the session and the fresh password. Starts nothing itself: the recorder follows the switch at its next look.
/// </summary>
internal sealed class ChangeOptionChainRecorderHandler(IOptionChainSwitchStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeOptionChainRecorderHandler> logger) : ICommandHandler<ChangeOptionChainRecorderCommand, OptionChainRecorderSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<OptionChainRecorderSwitched>> HandleAsync(ChangeOptionChainRecorderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            OptionChainLog.SwitchRefused(logger, "options.owner_only");
            return Result.Failure<OptionChainRecorderSwitched>(Error.Forbidden("options.owner_only", "Only the signed-in owner switches the option-chain recorder."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            OptionChainLog.SwitchRefused(logger, "options.reason_invalid");
            return Result.Failure<OptionChainRecorderSwitched>(Error.Validation("options.reason_invalid", "A change needs a reason of 1 to 500 characters."));
        }

        var row = await store.AppendAsync(command.Enabled, actor, reason, cancellationToken);
        OptionChainLog.SwitchChanged(logger, row.Id, row.Enabled);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.OptionChainRecorderSwitched, null, $"switch={row.Id} option_chain_recorder={(row.Enabled ? "on" : "off")}", actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // the row stands; the audit cross-index is a second write. Only the type of the failure is logged.
            OptionChainLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(new OptionChainRecorderSwitched(row.Id, row.Enabled));
    }
}
