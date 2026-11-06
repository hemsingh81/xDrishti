using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The owner's act of switching the automatic kill setting (spec CF-12 D3, part 12b, ADR 0219). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update). The endpoint is responsible for the session, the CSRF
/// token and the fresh password. It starts nothing and demotes nothing itself.
/// </summary>
internal sealed class ChangeKillSwitchHandler(IKillSwitchStore store, ILogger<ChangeKillSwitchHandler> logger) : ICommandHandler<ChangeKillSwitchCommand, KillSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<KillSwitched>> HandleAsync(ChangeKillSwitchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!KillActors.IsOwner(command.Actor))
        {
            KillLog.Refused(logger, KillErrors.OwnerOnlyCode);
            return Result.Failure<KillSwitched>(KillErrors.OwnerOnly());
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<KillSwitched>(Error.Validation(KillErrors.ReasonInvalidCode, "A change needs a reason of 1 to 500 characters."));
        }

        var id = await store.AppendAsync(command.Auto, command.Actor, reason, cancellationToken);
        KillLog.Switched(logger, id, command.Auto ? 1 : 0);
        return Result.Success(new KillSwitched(id, command.Auto));
    }
}
