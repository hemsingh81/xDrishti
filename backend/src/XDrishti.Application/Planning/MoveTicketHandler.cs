using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Moves a ticket along the table of <see cref="TicketLifecycle"/> (spec P7-03). An owner decision writes its approval row in the same transaction; of two concurrent moves from the same state one wins and the other is stale.
/// The handler has no path to a broker and none that approves without the owner.
/// </summary>
internal sealed class MoveTicketHandler(IPlanStore store, TimeProvider time, ILogger<MoveTicketHandler> logger) : ICommandHandler<MoveTicketCommand, TicketMoveOutcome>
{
    public async ValueTask<Result<TicketMoveOutcome>> HandleAsync(MoveTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500)
        {
            problems.Add(new FieldError("reason", "The reason is 1 to 500 characters."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is 1 to 100 characters."));
        }

        var modifying = command.To == TicketState.Modified;
        if (modifying != !string.IsNullOrWhiteSpace(command.Modifications) || command.Modifications?.Length > 1000)
        {
            problems.Add(new FieldError("modifications", "Moving to modified needs what was edited (up to 1000 characters); no other move takes it."));
        }

        if (command.ConfirmReason is not null && (command.To != TicketState.Approved || command.ConfirmReason.Length is 0 or > 500))
        {
            problems.Add(new FieldError("confirmReason", "A confirmation reason (1 to 500 characters) goes only with an approval."));
        }

        if (problems.Count > 0)
        {
            return Refuse(PlanErrors.Invalid(problems));
        }

        var item = await store.FindItemAsync(command.ItemId, cancellationToken);
        if (item is null)
        {
            return Refuse(PlanErrors.ItemNotFound(command.ItemId));
        }

        var owner = HypothesisRows.IsOwner(command.Actor);
        var refusals = TicketLifecycle.Refusals(item.State, command.To, new TicketFacts(owner ? MoveActor.Owner : MoveActor.System, item.Item.Group));
        if (refusals.Count > 0)
        {
            return Refuse(PlanErrors.MoveRefused(refusals));
        }

        ApprovalInput? approval = TicketLifecycle.IsOwnerDecision(command.To)
            ? new ApprovalInput(DecisionOf(command.To), command.Modifications, command.ConfirmReason, command.Cutoff)
            : null;
        var status = await store.MoveItemAsync(new ItemMove(command.ItemId, item.State, command.To, command.Actor, command.Reason, time.GetUtcNow(), approval), cancellationToken);
        if (status != MoveStatus.Created)
        {
            return Refuse(status == MoveStatus.NotFound ? PlanErrors.ItemNotFound(command.ItemId) : PlanErrors.Stale());
        }

        var fromCode = item.State.Code();
        var toCode = command.To.Code();
        PlanLog.TicketMoved(logger, command.ItemId, fromCode, toCode);
        return Result.Success(new TicketMoveOutcome(command.ItemId, command.To.Code()));
    }

    private static ApprovalDecision DecisionOf(TicketState to) => to switch
    {
        TicketState.Approved => ApprovalDecision.Approve,
        TicketState.Modified => ApprovalDecision.Modify,
        _ => ApprovalDecision.Reject,
    };

    private Result<TicketMoveOutcome> Refuse(Error error)
    {
        PlanLog.Refused(logger, error.Code);
        return Result.Failure<TicketMoveOutcome>(error);
    }
}
