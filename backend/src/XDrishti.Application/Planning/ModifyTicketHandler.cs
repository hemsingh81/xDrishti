using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The owner lowers a ticket's size for accounts it is already sized for (spec P7-10, ADR 0095). Validated by <see cref="TicketModification"/> (reductions only, paper allocations only, no new account, nothing else editable) and refused for an
/// account the checklist refuses outright. The ticket becomes Modified and is NOT approved: it must be approved afterwards, which re-validates the stored change and runs the same checklist. Before the plan's cutoff only.
/// </summary>
internal sealed class ModifyTicketHandler(
    IPlanStore store,
    IChecklistSource checklists,
    ICommandHandler<MoveTicketCommand, TicketMoveOutcome> mover,
    TimeProvider time,
    ILogger<ModifyTicketHandler> logger) : ICommandHandler<ModifyTicketCommand, ReviewDecisionOutcome>
{
    public async ValueTask<Result<ReviewDecisionOutcome>> HandleAsync(ModifyTicketCommand command, CancellationToken cancellationToken)
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

        var (plan, item) = found.Value;
        if (!TicketReview.IsOpen(plan, time.GetUtcNow()))
        {
            return Refuse(PlanErrors.Closed("The plan is not open for modification: it is closed or its review cutoff has passed."));
        }

        var refusals = TicketLifecycle.Refusals(item.State, TicketState.Modified, new TicketFacts(MoveActor.Owner, item.Item.Group));
        if (refusals.Count > 0)
        {
            return Refuse(PlanErrors.MoveRefused(refusals));
        }

        var valid = TicketModification.Validate([.. item.Item.Allocations.Select(a => (a.AccountId, a.Mode, a.Quantity))], command.Quantities);
        if (valid.IsFailure)
        {
            return Refuse(valid.Error);
        }

        // The account gate is re-read for the accounts being changed: a ticket a checklist refuses outright is not edited into something approvable.
        var results = await checklists.ForTicketAsync(item, cancellationToken);
        foreach (var change in valid.Value)
        {
            var result = results.FirstOrDefault(r => r.AccountId == TicketReview.Id(change.AccountId));
            if (result is null || result.Refused)
            {
                return Refuse(PlanErrors.ChecklistBlocked($"Account {TicketReview.Id(change.AccountId)} is refused by the checklist ({ChecklistCodes.AccountPaper}) or has no stored checklist; it cannot be modified."));
            }
        }

        var moved = await mover.HandleAsync(new MoveTicketCommand(command.ItemId, TicketState.Modified, command.Actor, command.Reason.Trim(), TicketModification.Serialize(valid.Value)), cancellationToken);
        if (moved.IsFailure)
        {
            return Refuse(moved.Error);
        }

        ReviewLog.Modified(logger, command.ItemId);
        return Result.Success(new ReviewDecisionOutcome(command.ItemId, TicketState.Modified.Code(), string.Empty, []));
    }

    private Result<ReviewDecisionOutcome> Refuse(Error error)
    {
        ReviewLog.Refused(logger, error.Code);
        return Result.Failure<ReviewDecisionOutcome>(error);
    }
}
