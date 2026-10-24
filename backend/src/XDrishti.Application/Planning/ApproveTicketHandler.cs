using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The owner approves one ticket (spec P7-10, ADR 0095). In order: an owner actor; the ticket is in the plan; the plan is open and before its cutoff; the table allows the move; a modified ticket's stored change still holds against its allocations;
/// the stored checklist of EVERY account the ticket is sized for, with the owner's confirmations applied, allows approval (unavailable never passes; only a named, reasoned confirmation for that account covers a check; the account gate cannot be covered).
/// Then the move is written through <see cref="MoveTicketCommand"/> (the existing owner path: one transaction, approval row and state row, the database re-checks the cutoff on its own clock). A second approval of the same ticket is stale or refused, never a second row.
/// Approved means watched; no order is placed and none exists.
/// </summary>
internal sealed class ApproveTicketHandler(
    IPlanStore store,
    IChecklistSource checklists,
    ICommandHandler<MoveTicketCommand, TicketMoveOutcome> mover,
    TimeProvider time,
    ILogger<ApproveTicketHandler> logger) : ICommandHandler<ApproveTicketCommand, ReviewDecisionOutcome>
{
    public async ValueTask<Result<ReviewDecisionOutcome>> HandleAsync(ApproveTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ReviewLookup.OwnerGuard(command.Actor) is { } notOwner)
        {
            return Refuse(notOwner);
        }

        var reason = ReviewLookup.Reason(command.Reason, "Approved by the owner after review.");
        if (reason.IsFailure)
        {
            return Refuse(reason.Error);
        }

        var found = await ReviewLookup.FindAsync(store, command.PlanId, command.ItemId, cancellationToken);
        if (found.IsFailure)
        {
            return Refuse(found.Error);
        }

        var (plan, item) = found.Value;
        if (!TicketReview.IsOpen(plan, time.GetUtcNow()))
        {
            return Refuse(PlanErrors.Closed("The plan is not open for approval: it is closed or its review cutoff has passed."));
        }

        var refusals = TicketLifecycle.Refusals(item.State, TicketState.Approved, new TicketFacts(MoveActor.Owner, item.Item.Group));
        if (refusals.Count > 0)
        {
            return Refuse(PlanErrors.MoveRefused(refusals));
        }

        if (item.State == TicketState.Modified)
        {
            var modification = TicketReview.Modification(item, await store.ApprovalsAsync(command.PlanId, cancellationToken));
            if (modification.IsFailure)
            {
                return Refuse(modification.Error);
            }
        }

        var results = await checklists.ForTicketAsync(item, cancellationToken);
        var reviews = TicketReview.Review(item, results, command.Confirmations, command.Actor);
        if (reviews.IsFailure)
        {
            return Refuse(reviews.Error);
        }

        if (TicketReview.Blocked(item, reviews.Value) is { } blocked)
        {
            return Refuse(PlanErrors.ChecklistBlocked(blocked));
        }

        var confirm = TicketReview.ConfirmText(item, reviews.Value);
        if (confirm.IsFailure)
        {
            return Refuse(confirm.Error);
        }

        var moved = await mover.HandleAsync(new MoveTicketCommand(command.ItemId, TicketState.Approved, command.Actor, reason.Value, null, confirm.Value), cancellationToken);
        if (moved.IsFailure)
        {
            return Refuse(moved.Error);
        }

        await ReviewProgress.EnterReviewAsync(store, command.PlanId, command.Actor, cancellationToken);
        var overridden = TicketReview.Overridden(reviews.Value);
        ReviewLog.Approved(logger, command.ItemId, overridden.Count);
        return Result.Success(new ReviewDecisionOutcome(command.ItemId, TicketState.Approved.Code(), PlanWording.ApprovedForWatching, overridden));
    }

    private Result<ReviewDecisionOutcome> Refuse(Error error)
    {
        ReviewLog.Refused(logger, error.Code);
        return Result.Failure<ReviewDecisionOutcome>(error);
    }
}
