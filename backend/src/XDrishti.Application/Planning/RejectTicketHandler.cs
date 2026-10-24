using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The owner rejects a ticket (spec P7-10). Rejecting is always safe, so it is allowed after the cutoff time too, until the cutoff job has closed the plan (the store then refuses it because the plan is no longer open for decisions).
/// </summary>
internal sealed class RejectTicketHandler(
    IPlanStore store,
    ICommandHandler<MoveTicketCommand, TicketMoveOutcome> mover,
    ILogger<RejectTicketHandler> logger) : ICommandHandler<RejectTicketCommand, ReviewDecisionOutcome>
{
    public async ValueTask<Result<ReviewDecisionOutcome>> HandleAsync(RejectTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ReviewLookup.OwnerGuard(command.Actor) is { } notOwner)
        {
            return Refuse(notOwner);
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 500)
        {
            return Refuse(PlanErrors.Invalid([new FieldError("reason", "The reason is 1 to 500 characters.")]));
        }

        var found = await ReviewLookup.FindAsync(store, command.PlanId, command.ItemId, cancellationToken);
        if (found.IsFailure)
        {
            return Refuse(found.Error);
        }

        var moved = await mover.HandleAsync(new MoveTicketCommand(command.ItemId, TicketState.Rejected, command.Actor, command.Reason.Trim()), cancellationToken);
        if (moved.IsFailure)
        {
            return Refuse(moved.Error);
        }

        await ReviewProgress.EnterReviewAsync(store, command.PlanId, command.Actor, cancellationToken);
        ReviewLog.Rejected(logger, command.ItemId);
        return Result.Success(new ReviewDecisionOutcome(command.ItemId, TicketState.Rejected.Code(), string.Empty, []));
    }

    private Result<ReviewDecisionOutcome> Refuse(Error error)
    {
        ReviewLog.Refused(logger, error.Code);
        return Result.Failure<ReviewDecisionOutcome>(error);
    }
}
