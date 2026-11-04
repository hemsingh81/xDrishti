using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-01 acceptance criterion 7.</summary>
public sealed class MinTrackRecordTests
{
    [Fact]
    public void Per_trade_sharpe_point_one_normal_ninety_five_percent_is_273()
    {
        // 1 + (1 + 0.5 * 0.01) * (1.6449 / 0.10)^2 = 272.9
        MinTrackRecord.Trades(0.10, 0, 0, 3, 0.95).ShouldBe(273);
    }

    [Fact]
    public void Negative_skew_lengthens_it()
    {
        MinTrackRecord.Trades(0.10, 0, -1, 3, 0.95)!.Value.ShouldBeGreaterThan(273);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-0.1, 0.0)]
    [InlineData(0.05, 0.05)]
    [InlineData(double.NaN, 0.0)]
    public void No_edge_to_detect_is_null(double sr, double star) => MinTrackRecord.Trades(sr, star, 0, 3, 0.95).ShouldBeNull();

    [Fact]
    public void A_bad_confidence_or_shape_is_null()
    {
        MinTrackRecord.Trades(0.1, 0, 0, 3, 1).ShouldBeNull();
        MinTrackRecord.Trades(0.1, 0, double.NaN, 3, 0.95).ShouldBeNull();
    }
}
