using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Hides one loosening on every plan read from now on (BK-10b): one insert-only row, no password (as the other dismissals of the plan screen), the actor the owner of the session. Order: the id is well formed (422), the change exists (404) and was a loosening (422), then
/// the row is added unless the change was dismissed already (204 either way) and one account event is written. The change row is never edited; no limit and no stored plan changes.
/// </summary>
internal sealed class DismissLooseningHandler(ILooseningStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<DismissLooseningHandler> logger) : ICommandHandler<DismissLooseningCommand>
{
    public async ValueTask<Result> HandleAsync(DismissLooseningCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Result.Failure(Error.Forbidden("plans.owner_only", "Only the signed-in owner dismisses a loosening."));
        }

        if (LooseningNoticeId.Parse(command.NoticeId) is not var (source, id))
        {
            return Result.Failure(Error.Validation(PlanErrors.InvalidCode, "The notice id must be sizing:{id}, budget:{id} or basket:{id}."));
        }

        var found = await store.FindAsync(source, id, cancellationToken);
        if (found is null)
        {
            return Result.Failure(Error.NotFound(PlanErrors.NotFoundCode, "No such loosening."));
        }

        if (found.Direction != LooseningDirections.Loosen)
        {
            return Result.Failure(Error.Validation(PlanErrors.InvalidCode, "That change did not loosen a limit; there is nothing to dismiss."));
        }

        if (await store.AddDismissalAsync(new NewLooseningDismissal(source, id, command.Actor, null, null), cancellationToken))
        {
            LooseningLog.Dismissed(logger, source, id, "owner");
            await LooseningAudit.RecordAsync(events, unitOfWork, clock, logger, AccountEventType.LooseningDismissed, string.Create(CultureInfo.InvariantCulture, $"notice={command.NoticeId} source={source} key={found.Notice.Key}"), command.Actor);
        }

        return Result.Success();
    }
}
