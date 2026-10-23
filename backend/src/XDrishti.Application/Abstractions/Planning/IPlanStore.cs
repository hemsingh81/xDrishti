namespace XDrishti.Application.Abstractions.Planning;

/// <summary>
/// The plan record (spec P7-03, ADR 0083): <c>trading.plans</c>, <c>plan_items</c>, <c>plan_item_allocations</c>, <c>plan_events</c>, <c>plan_runs</c> and <c>workflow.approvals</c> behind a port. Insert-only: a state change is a new row,
/// written under a lock per plan or item after checking that the newest state is still the one the caller saw. No member here places, modifies or cancels an order.
/// </summary>
public interface IPlanStore
{
    /// <summary>Stores a plan with its items, allocations and its Published row in one transaction, unless the plan key is already stored.</summary>
    ValueTask<PlanPublished> PublishAsync(NewPlan plan, CancellationToken cancellationToken);

    /// <summary>One plan with its current state, or null.</summary>
    ValueTask<PlanRow?> FindAsync(long planId, CancellationToken cancellationToken);

    /// <summary>Plans newest first, optionally for one session date and/or those with an allocation to one account.</summary>
    ValueTask<IReadOnlyList<PlanRow>> ListAsync(DateOnly? date, Guid? accountId, int limit, CancellationToken cancellationToken);

    /// <summary>The items of a plan in rank order with their current states.</summary>
    ValueTask<IReadOnlyList<PlanItemRow>> ItemsAsync(long planId, CancellationToken cancellationToken);

    /// <summary>One item with its current state, or null.</summary>
    ValueTask<PlanItemRow?> FindItemAsync(long itemId, CancellationToken cancellationToken);

    /// <summary>Every state row of a plan and its items, oldest first.</summary>
    ValueTask<IReadOnlyList<PlanEventRow>> HistoryAsync(long planId, CancellationToken cancellationToken);

    /// <summary>Writes an item state row (and the owner's approval row, in the same transaction) if the newest state is still <c>From</c>; of two concurrent moves from the same state one wins.</summary>
    ValueTask<MoveStatus> MoveItemAsync(ItemMove move, CancellationToken cancellationToken);

    /// <summary>Writes a plan state row if the newest state is still <c>From</c>.</summary>
    ValueTask<MoveStatus> MovePlanAsync(PlanMove move, CancellationToken cancellationToken);

    /// <summary>Records one attempt of the pipeline.</summary>
    ValueTask RecordRunAsync(NewPlanRun run, CancellationToken cancellationToken);
}
