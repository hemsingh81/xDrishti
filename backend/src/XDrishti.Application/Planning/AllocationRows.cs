using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;

namespace XDrishti.Application.Planning;

/// <summary>Maps an allocation to the rows of <c>trading.plan_item_allocations</c> (spec P7-07). Only paper rows with a quantity above 0 are produced; the database check repeats that.</summary>
public static class AllocationRows
{
    /// <summary>The rows to publish with a plan item.</summary>
    public static IReadOnlyList<NewAllocation> ToNew(AllocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return [.. result.Decisions.Where(d => d.IsAllocated && d.Mode == TradingMode.Paper).Select(d => new NewAllocation(d.AccountId, "paper", d.Quantity))];
    }
}
