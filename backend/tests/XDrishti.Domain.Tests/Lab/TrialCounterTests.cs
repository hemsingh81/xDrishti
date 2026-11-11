using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-06, D3 and AC5, worked by hand: N = hashes + extra exits + re-plans + nulls + sweep points; f = 1 + 0.25 log2(N).</summary>
public sealed class TrialCounterTests
{
    // 3 edits of a strategy, each full-tested with 4 exit candidates (3 extra each), one sweep of 16 points
    private static readonly TrialCounts Example = new(3, 16, 3 * TrialCounter.ExtraExits(4), 0, 0);

    [Fact]
    public void The_example_counts_28_and_the_factor_is_2_2018_where_the_old_rule_gave_19_and_2_0620()
    {
        Example.Total.ShouldBe(28);
        Example.DistinctHashes.ShouldBe(19);
        Math.Round(MultipleTesting.Factor(28), 4).ShouldBe(2.2018);
        Math.Round(MultipleTesting.Factor(19), 4).ShouldBe(2.0620);
    }

    [Fact]
    public void The_expectancy_gate_of_0_15_R_moves_from_0_3093_to_0_3303()
    {
        Math.Round(0.15 * MultipleTesting.Factor(19), 4).ShouldBe(0.3093);
        Math.Round(0.15 * MultipleTesting.Factor(28), 4).ShouldBe(0.3303);
    }

    [Theory]
    [InlineData(64, 2.5)]
    [InlineData(125, 2.7415)]
    [InlineData(27, 2.1887)]
    public void The_factor_of_the_registered_grids_is_as_worked_by_hand(int n, double factor)
    {
        // the spec's 2.7415 for 125 is the arithmetic with log2(125) rounded to 6.966; the exact value is 2.741446, so the check is within one unit of the fourth place
        MultipleTesting.Factor(n).ShouldBe(factor, 1.1e-4);
        Math.Round(0.15 * MultipleTesting.Factor(64), 4).ShouldBe(0.375);
    }

    [Fact]
    public void Five_to_the_third_and_three_to_the_third_are_the_grids_of_the_example()
    {
        ((int)Math.Pow(5, 3)).ShouldBe(125);
        ((int)Math.Pow(3, 3)).ShouldBe(27);
    }

    [Fact]
    public void Replans_and_nulls_add_one_each()
    {
        new TrialCounts(2, 0, 3, 2, 4).Total.ShouldBe(11);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void The_extra_exits_are_one_less_than_the_candidates_and_never_negative(int candidates, int extra)
    {
        TrialCounter.ExtraExits(candidates).ShouldBe(extra);
    }

    [Fact]
    public void The_verdict_uses_the_old_count_until_the_honest_one_is_switched_on()
    {
        TrialCounter.Judged(Example, honest: false).ShouldBe(19);
        TrialCounter.Judged(Example, honest: true).ShouldBe(28);
    }

    [Fact]
    public void A_part_below_zero_counts_as_zero()
    {
        new TrialCounts(-5, 2, 0, 0, 0).Total.ShouldBe(2);
    }

    [Fact]
    public void Each_system_replay_is_one_look_in_the_total_and_not_a_distinct_hash()
    {
        // SR-01 (ADR 0272), the stricter choice: repeated whole-system replays are looks. They are in Total, and DistinctHashes (what CountVariantsAsync counts) leaves them out.
        var parts = new TrialCounts(2, 1, 3, 2, 4, SystemReplays: 5);

        parts.Total.ShouldBe(17);
        parts.DistinctHashes.ShouldBe(3);
        TrialCounter.Judged(parts, honest: true).ShouldBe(17);
        TrialCounter.Judged(parts, honest: false).ShouldBe(3);
        new TrialCounts(0, 0, 0, 0, 0, SystemReplays: -2).Total.ShouldBe(0);
    }
}
