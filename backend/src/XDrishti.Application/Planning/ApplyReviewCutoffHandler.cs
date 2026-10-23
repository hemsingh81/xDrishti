using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The review cutoff (spec P7-03, doc 04 §4.2): unreviewed tickets lapse; the plan takes the state its tickets support (approved only if the owner approved a ticket). Lapsing is the default and the only thing the cutoff does to a ticket:
/// there is no option that approves, no top-N and no promotion from the reserve. Refused before the cutoff time and for a plan that is not Published or InReview.
/// </summary>
internal sealed class ApplyReviewCutoffHandler(IPlanStore store, TimeProvider time, ILogger<ApplyReviewCutoffHandler> logger) : ICommandHandler<ApplyReviewCutoffCommand, PlanMoveOutcome>
{
    private const string Reason = "Review cutoff passed without a decision.";

    public async ValueTask<Result<PlanMoveOutcome>> HandleAsync(ApplyReviewCutoffCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (NotAServiceActor(command.Actor))
        {
            return Refuse(PlanErrors.Invalid([new FieldError("actor", "The cutoff is a service's act, not the owner's.")]));
        }

        var plan = await store.FindAsync(command.PlanId, cancellationToken);
        if (plan is null)
        {
            return Refuse(PlanErrors.NotFound(command.PlanId));
        }

        if (time.GetUtcNow() < command.Cutoff)
        {
            return Refuse(PlanErrors.MoveRefused(["The review cutoff has not passed."]));
        }

        if (plan.State is not (PlanState.Published or PlanState.InReview))
        {
            return Refuse(PlanErrors.MoveRefused([$"A {plan.State.Code()} plan has no review left to close."]));
        }

        var items = await store.ItemsAsync(command.PlanId, cancellationToken);
        var decision = ReviewCutoff.Decide([.. items.Select(i => (i.Item.Group, i.State))]);
        var lapsed = 0;
        foreach (var index in decision.LapseIndexes)
        {
            var item = items[index];
            var to = TicketLifecycle.CutoffDefault(item.State)!.Value;
            var status = await store.MoveItemAsync(new ItemMove(item.Id, item.State, to, command.Actor, Reason, time.GetUtcNow(), null), cancellationToken);
            if (status == MoveStatus.Created)
            {
                lapsed++;
            }
        }

        // A ticket the owner decided on while the cutoff ran was left alone (stale), so read the states again before the plan moves.
        items = await store.ItemsAsync(command.PlanId, cancellationToken);
        var after = ReviewCutoff.Decide([.. items.Select(i => (i.Item.Group, i.State))]);
        var facts = PlanFacts.Of(items.Where(i => i.Item.Group != TicketGroup.Rejected).Select(i => i.State));
        var refusals = PlanLifecycle.Refusals(plan.State, after.Plan, facts);
        if (refusals.Count > 0)
        {
            return Refuse(PlanErrors.MoveRefused(refusals));
        }

        var moved = await store.MovePlanAsync(new PlanMove(command.PlanId, plan.State, after.Plan, command.Actor, Reason, time.GetUtcNow()), cancellationToken);
        if (moved != MoveStatus.Created)
        {
            return Refuse(PlanErrors.Stale());
        }

        var planCode = after.Plan.Code();
        PlanLog.CutoffApplied(logger, command.PlanId, lapsed, planCode);
        return Result.Success(new PlanMoveOutcome(command.PlanId, after.Plan.Code(), lapsed));
    }

    private static bool NotAServiceActor(string actor) => string.IsNullOrWhiteSpace(actor) || actor.Length > 100 || XDrishti.Application.Learning.HypothesisRows.IsOwner(actor);

    private Result<PlanMoveOutcome> Refuse(Error error)
    {
        PlanLog.Refused(logger, error.Code);
        return Result.Failure<PlanMoveOutcome>(error);
    }
}
