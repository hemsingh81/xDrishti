using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;

namespace XDrishti.Application.Tests.Planning;

/// <summary>The rows handed to the plan store (spec P7-07): paper only, quantity above 0.</summary>
public sealed class AllocationRowsTests
{
    [Fact]
    public void Only_allocated_paper_accounts_become_rows()
    {
        var paper = Guid.NewGuid();
        var live = Guid.NewGuid();
        var none = Guid.NewGuid();
        var result = PerAccountAllocator.Allocate(
            new AllocationTicket("AAA", "BUY", 100, 98, 1000, 1),
            [
                new AllocationAccount(paper, TradingMode.Paper, true, 1_000_000, 0.5, 20, 3, 1, 500_000, false),
                new AllocationAccount(live, TradingMode.ManualLive, true, 1_000_000, 0.5, 20, 3, 1, 500_000, false),
                new AllocationAccount(none, TradingMode.Paper, true, 1_000_000, 0.5, 20, 3, 1, 500_000, true),
            ]);

        var rows = AllocationRows.ToNew(result);

        rows.Count.ShouldBe(1);
        rows[0].AccountId.ShouldBe(paper);
        rows[0].Mode.ShouldBe("paper");
        rows[0].Quantity.ShouldBe(1000);
    }

    [Fact]
    public void A_live_row_carries_its_scale_and_ramp_event_and_one_without_them_is_never_written()
    {
        var live = Guid.NewGuid();
        var unstepped = Guid.NewGuid();
        var result = PerAccountAllocator.Allocate(
            new AllocationTicket("AAA", "BUY", 200, 195, 1000, 1),
            [
                new AllocationAccount(live, TradingMode.ManualLive, true, 100_000, 0.125, 6.25, 0.75, 0, 100_000, false, 0.25, 7),
                new AllocationAccount(unstepped, TradingMode.ManualLive, true, 100_000, 0.5, 25, 3, 0, 100_000, false),
            ],
            PoolMode.Live);

        var rows = AllocationRows.ToNew(result);

        rows.ShouldBe([new NewAllocation(live, "manual_live", 25, 0.25, 7)]);
    }
}
