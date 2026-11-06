using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The owner's act of switching <c>confidence.readiness.enforce</c> (spec CF-11 D6, ADR 0221). Refuses a non-owner actor and a missing or over-long reason before anything is written; appends one row (never an update). The endpoint is responsible for the session, the CSRF token
/// and the fresh password, ON or OFF alike (turning enforcement OFF removes a refusal, so it raises risk; ADR 0221). It moves no cell and starts nothing.
/// </summary>
internal sealed class ChangeReadinessSwitchHandler(IReadinessSwitchStore store, ILogger<ChangeReadinessSwitchHandler> logger) : ICommandHandler<ChangeReadinessSwitchCommand, ReadinessSwitched>
{
    public const int MaxReasonLength = 500;

    public async ValueTask<Result<ReadinessSwitched>> HandleAsync(ChangeReadinessSwitchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!KillActors.IsOwner(command.Actor))
        {
            ReadinessLog.Refused(logger, KillErrors.OwnerOnlyCode);
            return Result.Failure<ReadinessSwitched>(KillErrors.OwnerOnly());
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReasonLength)
        {
            return Result.Failure<ReadinessSwitched>(Error.Validation(KillErrors.ReasonInvalidCode, "A change needs a reason of 1 to 500 characters."));
        }

        var id = await store.AppendAsync(command.Enforce, command.Actor, reason, cancellationToken);
        ReadinessLog.Switched(logger, id, command.Enforce ? 1 : 0);
        return Result.Success(new ReadinessSwitched(id, command.Enforce));
    }
}
