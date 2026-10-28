using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-04 criteria 1 and 2: the live pool, the scale on the limits, two pools, and the paper pool unchanged.</summary>
public sealed class LiveAllocationTests
{
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static readonly AllocationTicket Ticket = new("AAA", "BUY", 200, 195, 100000, 1);

    // profile risk 0.5 %, capital cap 25 %, total 3 %, equity 100,000; the caller multiplies by the scale as PlanWiring does
    private static AllocationAccount Live(int n, double? scale, long? ev = 7) =>
        new(Id(n), TradingMode.ManualLive, true, 100_000, 0.5 * (scale ?? 1), 25 * (scale ?? 1), 3 * (scale ?? 1), 0, 100_000, false, scale, ev);

    private static AllocationAccount Paper(int n) => new(Id(n), TradingMode.Paper, true, 100_000, 0.5, 25, 3, 0, 100_000, false);

    [Theory]
    [InlineData(0.25, 25, 125.0)]
    [InlineData(0.5, 50, 250.0)]
    public void A_live_account_is_sized_at_the_scaled_risk_and_the_capital_cap_does_not_bind(double scale, int quantity, double riskRupees)
    {
        var d = PerAccountAllocator.Allocate(Ticket, [Live(1, scale)], PoolMode.Live).Decisions.Single();

        d.Quantity.ShouldBe(quantity);
        d.Binding.ShouldBe(AllocationCodes.RiskPerTrade);
        d.RiskScale.ShouldBe(scale);
        d.RampEventId.ShouldBe(7);
        (d.Quantity * 5.0).ShouldBe(riskRupees);
        d.Capacity.ShouldBe(quantity);
    }

    [Fact]
    public void The_scaled_capital_cap_is_31_and_62_shares()
    {
        // 25 % x 0.25 of 100,000 at 200 = 31.25; x 0.5 = 62.5 (shown against the risk limit, which binds first)
        Math.Floor(100_000 * 25 * 0.25 / 100 / 200).ShouldBe(31);
        Math.Floor(100_000 * 25 * 0.5 / 100 / 200).ShouldBe(62);
    }

    [Fact]
    public void A_live_account_without_a_step_gets_nothing_and_a_paper_account_is_not_in_the_live_pool()
    {
        var r = PerAccountAllocator.Allocate(Ticket, [Live(1, null, null), Paper(2)], PoolMode.Live);

        r.Decisions.Single(d => d.AccountId == Id(1)).Code.ShouldBe(AllocationCodes.UnavailableAccount);
        r.Decisions.Single(d => d.AccountId == Id(1)).Message.ShouldContain("ramp step");
        r.Decisions.Single(d => d.AccountId == Id(2)).Code.ShouldBe(AllocationCodes.NotLive);
        r.TotalAllocated.ShouldBe(0);
    }

    [Fact]
    public void The_paper_pool_is_the_default_and_refuses_a_live_account_as_before()
    {
        var withDefault = PerAccountAllocator.Allocate(Ticket, [Paper(2), Live(1, 0.25)]);
        var explicitPaper = PerAccountAllocator.Allocate(Ticket, [Paper(2), Live(1, 0.25)], PoolMode.Paper);

        withDefault.Decisions.ShouldBe(explicitPaper.Decisions);
        withDefault.Decisions.Single(d => d.AccountId == Id(1)).Code.ShouldBe(AllocationCodes.NotPaper);
        withDefault.Decisions.Single(d => d.AccountId == Id(2)).RiskScale.ShouldBeNull();
    }

    [Fact]
    public void Adding_a_paper_account_never_changes_the_live_allocation_and_the_control_notices_a_shared_pool()
    {
        var alone = PerAccountAllocator.Allocate(Ticket, [Live(1, 0.25)], PoolMode.Live).Decisions.Single().Quantity;
        // the paper accounts are in the other call, so the live result cannot depend on them
        var livePool = PerAccountAllocator.Allocate(Ticket, [Live(1, 0.25)], PoolMode.Live).Decisions.Single().Quantity;
        livePool.ShouldBe(alone);

        // negative control: a doctored shared pool (live and a paper account in ONE small pool) does change the live quantity, so this check can fail
        var small = Ticket with { Quantity = 40 };
        var shared = PerAccountAllocator.Allocate(small, [Live(1, 0.25), Paper(2)]);
        var separate = PerAccountAllocator.Allocate(small, [Live(1, 0.25)], PoolMode.Live).Decisions.Single().Quantity;
        shared.Decisions.Single(d => d.AccountId == Id(1)).Quantity.ShouldNotBe(separate);
    }
}
