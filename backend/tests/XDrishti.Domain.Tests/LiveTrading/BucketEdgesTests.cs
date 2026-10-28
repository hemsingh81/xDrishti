using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criterion 5: the liquidity bucket of a turnover in crore, at every boundary and for an unknown turnover.</summary>
public sealed class BucketEdgesTests
{
    [Theory]
    [InlineData(640, "L1")]
    [InlineData(500, "L1")]
    [InlineData(499.99, "L2")]
    [InlineData(150, "L2")]
    [InlineData(149.99, "L3")]
    [InlineData(50, "L3")]
    [InlineData(49.99, "L4")]
    [InlineData(10, "L4")]
    [InlineData(9.99, "L5")]
    [InlineData(0.5, "L5")]
    public void The_default_edges_put_a_turnover_in_its_bucket(double crore, string expected) =>
        BucketEdges.Default.Classify((decimal)crore).ShouldBe(expected);

    [Fact]
    public void An_unknown_or_non_positive_turnover_is_in_no_bucket()
    {
        BucketEdges.Default.Classify(null).ShouldBe(LiquidityBucket.Unknown);
        BucketEdges.Default.Classify(0).ShouldBe(LiquidityBucket.Unknown);
        BucketEdges.Default.Classify(-3).ShouldBe(LiquidityBucket.Unknown);
    }

    [Fact]
    public void Edges_must_be_three_to_six_strictly_ascending_positive_values()
    {
        BucketEdges.Create([10m, 50m, 150m]).ShouldNotBeNull().Buckets.ShouldBe(["L1", "L2", "L3", "L4", "unknown"]);
        BucketEdges.Create([1m, 2m, 3m, 4m, 5m, 6m]).ShouldNotBeNull().Classify(0.5m).ShouldBe("L7");
        BucketEdges.Create([10m, 50m]).ShouldBeNull();
        BucketEdges.Create([1m, 2m, 3m, 4m, 5m, 6m, 7m]).ShouldBeNull();
        BucketEdges.Create([10m, 10m, 50m]).ShouldBeNull();
        BucketEdges.Create([50m, 10m, 150m]).ShouldBeNull();
        BucketEdges.Create([0m, 10m, 50m]).ShouldBeNull();
    }
}
