using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 part 1, AC8 (ADR 0221): the system level is the minimum of the integrity and the capital-weighted mean of the strategy scores.</summary>
public sealed class SystemReadinessTests
{
    private static readonly ReadinessOptions Options = ReadinessInputsFixture.Options;

    [Fact]
    public void Two_strategies_with_risk_shares_60_and_40_and_scores_72_and_30_give_55_2_building_and_name_B()
    {
        var system = SystemReadinessCalculator.Compute([new("A", 72, "Evidence", 0.6), new("B", 30, "Power at 0.3 R", 0.4)], FaultState.Clear, Options);

        system.Score.ShouldBe(55.2, 1e-9);
        system.Label.ShouldBe(ReadinessLabel.Building);
        system.WeakestStrategy.ShouldBe("B");
        system.WeakestSubScore.ShouldBe("Power at 0.3 R");
        system.EqualWeights.ShouldBeFalse();
    }

    [Fact]
    public void An_integrity_fault_gives_0()
    {
        var system = SystemReadinessCalculator.Compute([new("A", 72, null, 0.6), new("B", 30, null, 0.4)], FaultState.Fault, Options);

        system.Score.ShouldBe(0);
        system.Label.ShouldBe(ReadinessLabel.NotReady);
        system.Reason.ShouldNotBeNull();
    }

    [Fact]
    public void An_unreadable_integrity_is_not_a_pass()
    {
        SystemReadinessCalculator.Compute([new("A", 90, null, null)], FaultState.Unavailable, Options).Score.ShouldBe(0);
    }

    [Fact]
    public void With_no_risk_shares_the_weights_are_equal_and_labelled()
    {
        var system = SystemReadinessCalculator.Compute([new("A", 72, null, null), new("B", 30, null, null)], FaultState.Clear, Options);

        system.Score.ShouldBe(51, 1e-9);
        system.EqualWeights.ShouldBeTrue();
    }

    [Fact]
    public void A_missing_share_on_any_strategy_makes_all_the_weights_equal()
    {
        var system = SystemReadinessCalculator.Compute([new("A", 72, null, 0.9), new("B", 30, null, null)], FaultState.Clear, Options);

        system.Score.ShouldBe(51, 1e-9);
        system.EqualWeights.ShouldBeTrue();
    }

    [Fact]
    public void Risk_shares_need_not_add_to_one()
    {
        var system = SystemReadinessCalculator.Compute([new("A", 80, null, 3), new("B", 40, null, 1)], FaultState.Clear, Options);

        system.Score.ShouldBe(70, 1e-9);
    }

    [Fact]
    public void No_strategy_is_not_ready_and_says_so()
    {
        var system = SystemReadinessCalculator.Compute([], FaultState.Clear, Options);

        (system.Score, system.Label, system.Strategies, system.Reason).ShouldBe((0, ReadinessLabel.NotReady, 0, "no strategy yet"));
    }

    [Fact]
    public void A_tie_names_the_strategy_first_in_order()
    {
        SystemReadinessCalculator.Compute([new("b", 10, null, null), new("a", 10, null, null)], FaultState.Clear, Options).WeakestStrategy.ShouldBe("a");
    }
}
