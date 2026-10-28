using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;

namespace XDrishti.Application.Planning;

/// <summary>Maps an allocation to the rows of <c>trading.plan_item_allocations</c> (spec P7-07). Only rows with a quantity above 0 are produced; the database check repeats that. Caveat: see <see cref="AllocationResult.Caveat"/> (pool split by each account's own limits only; no cross-account caps, replicate/distribute or liquidity cap; funds not margin-aware for SELL); whoever publishes these rows must show it.</summary>
public static class AllocationRows
{
    /// <summary>The rows to publish with a plan item: paper rows, and manual-live rows that carry their ramp scale and event (the database repeats both rules).</summary>
    public static IReadOnlyList<NewAllocation> ToNew(AllocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return
        [
            .. result.Decisions.Where(d => d.IsAllocated && d.Mode == TradingMode.Paper).Select(d => new NewAllocation(d.AccountId, "paper", d.Quantity)),
            .. result.Decisions.Where(d => d.IsAllocated && d.Mode == TradingMode.ManualLive && d.RiskScale is > 0 and <= 1 && d.RampEventId is not null).Select(d => new NewAllocation(d.AccountId, "manual_live", d.Quantity, d.RiskScale, d.RampEventId)),
        ];
    }
}
