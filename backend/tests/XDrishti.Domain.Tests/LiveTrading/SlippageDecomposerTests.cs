using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criteria 1 and 2: the entry slippage split into the delay before the order and the execution, positive always worse for the owner.</summary>
public sealed class SlippageDecomposerTests
{
    [Fact]
    public void A_buy_entry_is_9_5_ticks_3_8_bp_and_0_0475_R_and_the_parts_add_up_exactly()
    {
        var p = SlippageDecomposer.Decompose(Side.Buy, 1250.00m, 1250.20m, 1250.475m, 0.05m, 10.00m).ShouldNotBeNull();
        p.Total.ShouldBe(0.475m);
        p.Delay.ShouldBe(0.20m);
        p.Execution.ShouldBe(0.275m);
        (p.Delay + p.Execution).ShouldBe(p.Total);
        p.TotalTicks.ShouldNotBeNull().ShouldBe(9.5, 1e-9);
        p.TotalBps.ShouldBe(3.8, 1e-9);
        p.TotalR.ShouldNotBeNull().ShouldBe(0.0475, 1e-9);
        p.DelayR.ShouldNotBeNull().ShouldBe(0.02, 1e-9);
        p.ExecutionR.ShouldNotBeNull().ShouldBe(0.0275, 1e-9);
    }

    [Fact]
    public void A_sell_entry_mirrors_it()
    {
        var p = SlippageDecomposer.Decompose(Side.Sell, 800.00m, 799.90m, 799.70m, 0.05m, 5m).ShouldNotBeNull();
        p.Total.ShouldBe(0.30m);
        p.Delay.ShouldBe(0.10m);
        p.Execution.ShouldBe(0.20m);
    }

    [Fact]
    public void A_fill_better_than_the_reference_is_negative_and_a_negative_delay_still_adds_up()
    {
        var p = SlippageDecomposer.Decompose(Side.Buy, 100.00m, 99.90m, 100.05m, 0.05m, 1m).ShouldNotBeNull();
        p.Delay.ShouldBe(-0.10m);
        p.Execution.ShouldBe(0.15m);
        p.Total.ShouldBe(0.05m);
        (p.Delay + p.Execution).ShouldBe(p.Total);
        SlippageDecomposer.Decompose(Side.Buy, 100m, null, 99.5m, 0.05m, 1m).ShouldNotBeNull().Total.ShouldBe(-0.5m);
    }

    [Fact]
    public void No_arrival_gives_a_total_but_no_delay_or_execution_never_zero()
    {
        var p = SlippageDecomposer.Decompose(Side.Buy, 1250m, null, 1250.475m, 0.05m, 10m).ShouldNotBeNull();
        p.Total.ShouldBe(0.475m);
        p.Delay.ShouldBeNull();
        p.Execution.ShouldBeNull();
        p.DelayR.ShouldBeNull();
        p.ExecutionR.ShouldBeNull();
    }

    [Fact]
    public void No_tick_or_no_stop_leaves_ticks_and_R_null_and_no_reference_gives_nothing()
    {
        var p = SlippageDecomposer.Decompose(Side.Buy, 1250m, 1250.2m, 1250.475m, null, null).ShouldNotBeNull();
        p.TotalTicks.ShouldBeNull();
        p.TotalR.ShouldBeNull();
        p.TotalBps.ShouldBe(3.8, 1e-9);
        SlippageDecomposer.Decompose(Side.Buy, 0m, null, 100m, 0.05m, 1m).ShouldBeNull();
    }
}
