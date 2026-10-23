using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>Moves a plan along <see cref="PlanLifecycle"/>, checked against the current states of its tickets (spec P7-03).</summary>
internal sealed class MovePlanHandler(IPlanStore store, TimeProvider time, ILogger<MovePlanHandler> logger) : ICommandHandler<MovePlanCommand, PlanMoveOutcome>
{
    public async ValueTask<Result<PlanMoveOutcome>> HandleAsync(MovePlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500 || string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Refuse(PlanErrors.Invalid([new FieldError("reason", "The reason is 1 to 500 characters and the actor 1 to 100.")]));
        }

        var plan = await store.FindAsync(command.PlanId, cancellationToken);
        if (plan is null)
        {
            return Refuse(PlanErrors.NotFound(command.PlanId));
        }

        var items = await store.ItemsAsync(command.PlanId, cancellationToken);
        var facts = PlanFacts.Of(items.Where(i => i.Item.Group != TicketGroup.Rejected).Select(i => i.State));
        var refusals = PlanLifecycle.Refusals(plan.State, command.To, facts);
        if (refusals.Count > 0)
        {
            return Refuse(PlanErrors.MoveRefused(refusals));
        }

        var status = await store.MovePlanAsync(new PlanMove(command.PlanId, plan.State, command.To, command.Actor, command.Reason, time.GetUtcNow()), cancellationToken);
        if (status != MoveStatus.Created)
        {
            return Refuse(status switch
            {
                MoveStatus.NotFound => PlanErrors.NotFound(command.PlanId),
                MoveStatus.Refused => PlanErrors.MoveRefused(["The tickets of the plan no longer support this move."]),
                _ => PlanErrors.Stale(),
            });
        }

        var fromCode = plan.State.Code();
        var toCode = command.To.Code();
        PlanLog.PlanMoved(logger, command.PlanId, fromCode, toCode);
        return Result.Success(new PlanMoveOutcome(command.PlanId, command.To.Code(), 0));
    }

    private Result<PlanMoveOutcome> Refuse(Error error)
    {
        PlanLog.Refused(logger, error.Code);
        return Result.Failure<PlanMoveOutcome>(error);
    }
}
