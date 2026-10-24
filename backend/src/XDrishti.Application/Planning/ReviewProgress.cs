using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>Moves a published plan to InReview after the owner's first decision (spec P7-10), through the same store and lifecycle table as any plan move. Best effort: the decision is already stored, and a refused or stale plan move changes nothing.</summary>
internal static class ReviewProgress
{
    public static async ValueTask EnterReviewAsync(IPlanStore store, long planId, string actor, CancellationToken cancellationToken)
    {
        var plan = await store.FindAsync(planId, cancellationToken);
        if (plan is not { State: PlanState.Published })
        {
            return;
        }

        var items = await store.ItemsAsync(planId, cancellationToken);
        var facts = PlanFacts.Of(items.Where(i => i.Item.Group != TicketGroup.Rejected).Select(i => i.State));
        if (PlanLifecycle.Refusals(PlanState.Published, PlanState.InReview, facts).Count == 0)
        {
            await store.MovePlanAsync(new PlanMove(planId, PlanState.Published, PlanState.InReview, actor, "The owner decided on a ticket."), cancellationToken);
        }
    }
}
