using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>CF-05 criterion 6 (tiers): the proposed cuts Rs 100 crore and Rs 20 crore, the 20-day median, tier C and unavailable never priced.</summary>
public sealed class LiquidityTiersTests
{
    private const decimal ACut = 100 * LiquidityTiers.Crore;

    private const decimal BCut = 20 * LiquidityTiers.Crore;

    [Theory]
    [InlineData(450, LiquidityTier.A)]
    [InlineData(100, LiquidityTier.A)]
    [InlineData(60, LiquidityTier.B)]
    [InlineData(20, LiquidityTier.B)]
    [InlineData(12, LiquidityTier.C)]
    public void The_spec_names_are_in_their_tiers(double crore, LiquidityTier expected)
    {
        LiquidityTiers.Of((decimal)crore * LiquidityTiers.Crore, ACut, BCut).ShouldBe(expected);
    }

    [Fact]
    public void No_median_is_unavailable_and_neither_it_nor_tier_c_is_priced()
    {
        LiquidityTiers.Of(null, ACut, BCut).ShouldBe(LiquidityTier.Unavailable);
        LiquidityTiers.Bps(LiquidityTier.A, 3, 8).ShouldBe(3);
        LiquidityTiers.Bps(LiquidityTier.B, 3, 8).ShouldBe(8);
        LiquidityTiers.Bps(LiquidityTier.C, 3, 8).ShouldBeNull();
        LiquidityTiers.Bps(LiquidityTier.Unavailable, 3, 8).ShouldBeNull();
    }

    [Fact]
    public void The_median_is_of_the_last_twenty_sessions_and_needs_twenty()
    {
        LiquidityTiers.Median([.. Enumerable.Repeat(1m, 19)]).ShouldBeNull();

        // 25 values 1..25: the last twenty are 6..25, the median is (15 + 16) / 2
        LiquidityTiers.Median([.. Enumerable.Range(1, 25).Select(i => (decimal)i)]).ShouldBe(15.5m);
        LiquidityTiers.Median([.. Enumerable.Repeat(1m, 19), -1m]).ShouldBeNull("a negative value is not a traded value");
    }

    [Fact]
    public void A_single_outlier_session_does_not_move_the_median()
    {
        var values = Enumerable.Repeat(450m * LiquidityTiers.Crore, 19).Append(9_999m * LiquidityTiers.Crore).ToList();

        LiquidityTiers.Of(LiquidityTiers.Median(values), ACut, BCut).ShouldBe(LiquidityTier.A);
        LiquidityTiers.Median(values).ShouldBe(450m * LiquidityTiers.Crore);
    }
}
