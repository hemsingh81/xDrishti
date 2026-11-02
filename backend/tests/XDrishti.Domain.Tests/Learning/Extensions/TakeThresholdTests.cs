using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P13-05: the shared threshold search keeps the behaviour that <c>MetaPolicyEvaluator</c> had (its own tests stay unmodified).</summary>
public sealed class TakeThresholdTests
{
    [Fact]
    public void The_lowest_grid_value_with_the_best_sum_wins_under_the_kept_share_floor()
    {
        (double Score, double R)[] rows = [(0.1, -1), (0.1, -1), (0.5, 0.2), (0.5, 0.2), (0.9, 1), (0.9, 1)];

        var (threshold, share) = TakeThreshold.Choose(rows, 0.05, 0.5);

        threshold.ShouldBe(0.15, 1e-9);
        share.ShouldBe(4 / 6.0);
    }

    [Fact]
    public void A_floor_that_only_taking_everything_meets_gives_zero()
    {
        (double Score, double R)[] rows = [(0.1, 1), (0.2, 1), (0.3, 1), (0.4, 1)];

        TakeThreshold.Choose(rows, 0.05, 1.0).ShouldBe((0.0, 1.0));
    }
}
