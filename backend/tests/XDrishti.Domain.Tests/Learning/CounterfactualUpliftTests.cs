using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The cumulative take-R uplift (spec P8-12, criterion 4): a hand table, identical sides, independence of windows.</summary>
public sealed class CounterfactualUpliftTests
{
    [Fact]
    public void The_hand_table_gives_plus_2_4_and_minus_1_5_and_a_cumulative_of_0_9()
    {
        var points = CounterfactualUplift.Compute([new UpliftWindow(40, 0.10, 0.16, true), new UpliftWindow(50, 0.12, 0.09, true)]);

        points[0].Uplift.ShouldNotBeNull().ShouldBe(2.4, 1e-9);
        points[1].Uplift.ShouldNotBeNull().ShouldBe(-1.5, 1e-9);
        points[0].Cumulative.ShouldBe(2.4, 1e-9);
        points[1].Cumulative.ShouldBe(0.9, 1e-9);
    }

    [Fact]
    public void A_window_that_is_not_informative_has_no_uplift_and_adds_nothing()
    {
        var points = CounterfactualUplift.Compute([new UpliftWindow(30, 0.2, 0.2, false), new UpliftWindow(10, 0.0, 0.1, true)]);

        points[0].Uplift.ShouldBeNull();
        points[0].Cumulative.ShouldBe(0);
        points[1].Cumulative.ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void Identical_sides_give_zero_and_an_added_window_changes_no_other_window()
    {
        var same = CounterfactualUplift.Compute([new UpliftWindow(25, 0.3, 0.3, true)]);
        var one = CounterfactualUplift.Compute([new UpliftWindow(40, 0.10, 0.16, true)]);
        var two = CounterfactualUplift.Compute([new UpliftWindow(40, 0.10, 0.16, true), new UpliftWindow(5, 0.0, 1.0, true)]);

        same[0].Uplift.ShouldBe(0);
        two[0].ShouldBe(one[0]);
    }
}
