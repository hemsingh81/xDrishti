namespace XDrishti.Domain.Allocation;

/// <summary>The split of one ticket: one decision per supplied account, ordered by account id.</summary>
/// <param name="Decisions">Every account once.</param>
/// <param name="TicketQuantity">The ticket's quantity as supplied; null when unavailable.</param>
public sealed record AllocationResult(IReadOnlyList<AccountAllocation> Decisions, int? TicketQuantity)
{
    /// <summary>What this split does not do; shown wherever the result is shown.</summary>
    public const string Caveat = "pool split by each account's own limits only; no cross-account caps, replicate/distribute or liquidity cap; funds not margin-aware for SELL";

    /// <summary>Sum of the allocated quantities; never more than the ticket's quantity.</summary>
    public long TotalAllocated => Decisions.Sum(d => (long)d.Quantity);

    /// <summary>The decisions that got a quantity: the rows for <c>plan_item_allocations</c> (paper only, quantity above 0).</summary>
    public IReadOnlyList<AccountAllocation> Allocated => [.. Decisions.Where(d => d.IsAllocated)];
}
