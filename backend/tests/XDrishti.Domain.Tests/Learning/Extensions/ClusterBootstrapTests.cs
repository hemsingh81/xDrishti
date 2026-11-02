using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>The cluster bootstrap of a difference of two means (P15-10), on a hand case.</summary>
public sealed class ClusterBootstrapTests
{
    [Fact]
    public void Blocks_that_all_show_the_same_gap_give_that_gap_at_every_bound()
    {
        // Each block: A mean 1.0 (2 trades), B mean 0.4 (2 trades).
        DifferenceBlock[] blocks = [.. Enumerable.Range(0, 5).Select(_ => new DifferenceBlock(2.0, 2, 0.8, 2))];

        var bounds = ClusterBootstrap.Difference(blocks, 200, 1337, 0.9);

        bounds.Point!.Value.ShouldBe(0.6, 1e-12);
        bounds.Lower!.Value.ShouldBe(0.6, 1e-12);
        bounds.Upper!.Value.ShouldBe(0.6, 1e-12);
    }

    [Fact]
    public void Mixed_blocks_give_a_lower_bound_below_the_point_and_the_same_seed_gives_the_same_bounds()
    {
        DifferenceBlock[] blocks = [new(3, 2, 0, 2), new(0, 2, 0, 2), new(2, 2, 1, 2), new(-1, 2, 1, 2), new(4, 2, 0, 2)];

        var a = ClusterBootstrap.Difference(blocks, 500, 1337, 0.9);
        var b = ClusterBootstrap.Difference(blocks, 500, 1337, 0.9);

        a.ShouldBe(b);
        a.Lower!.Value.ShouldBeLessThan(a.Point!.Value);
        a.Upper!.Value.ShouldBeGreaterThan(a.Point.Value);
    }

    [Fact]
    public void One_block_or_an_empty_group_has_no_interval()
    {
        ClusterBootstrap.Difference([new DifferenceBlock(1, 1, 0, 1)], 100, 1, 0.9).Lower.ShouldBeNull();
        ClusterBootstrap.Difference([new DifferenceBlock(1, 1, 0, 0), new DifferenceBlock(1, 1, 0, 0)], 100, 1, 0.9).Point.ShouldBeNull();
    }
}
