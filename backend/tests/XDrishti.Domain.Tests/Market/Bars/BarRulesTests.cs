using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Market.Bars;

public sealed class BarRulesTests
{
    private const long Minute = 1_711_943_100; // 2024-04-01 03:45 UTC = 09:15 India

    [Fact]
    public void A_consistent_bar_is_valid_and_unchanged()
    {
        var bar = new Bar(Minute, 100, 102, 99, 101, 5);

        BarRules.Check(bar, out var result).ShouldBe(BarVerdict.Valid);

        result.ShouldBe(bar);
    }

    [Fact]
    public void A_zero_volume_minute_is_valid()
    {
        BarRules.Check(new Bar(Minute, 100, 100, 100, 100, 0), out _).ShouldBe(BarVerdict.Valid);
    }

    [Theory]
    [InlineData(105, 102, 99, 101, 105, 99)]   // the open is above the high (the pre-open price on the opening bar)
    [InlineData(100, 102, 99, 98, 102, 98)]    // the close is below the low
    [InlineData(100, 98, 99, 101, 101, 98)]    // high below low: both are widened
    [InlineData(95, 102, 99, 101, 102, 95)]    // the open is below the low
    public void A_bar_whose_high_and_low_do_not_contain_open_and_close_is_repaired_to_the_extremes(double open, double high, double low, double close, double expectedHigh, double expectedLow)
    {
        var verdict = BarRules.Check(new Bar(Minute, open, high, low, close, 1), out var result);

        verdict.ShouldBe(BarVerdict.Repaired);
        (result.High, result.Low, result.Open, result.Close).ShouldBe((expectedHigh, expectedLow, open, close));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, -1)]
    public void A_price_that_is_not_above_zero_is_rejected(double open, double high, double low, double close)
    {
        BarRules.Check(new Bar(Minute, open, high, low, close, 1), out _).ShouldBe(BarVerdict.NonPositivePrice);
    }

    [Fact]
    public void A_negative_volume_is_rejected()
    {
        BarRules.Check(new Bar(Minute, 1, 1, 1, 1, -1), out _).ShouldBe(BarVerdict.NegativeVolume);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_price_that_is_not_a_finite_number_is_rejected(double price)
    {
        BarRules.Check(new Bar(Minute, price, 1, 1, 1, 1), out _).ShouldBe(BarVerdict.NotANumber);
    }

    [Fact]
    public void A_time_that_is_not_a_whole_minute_is_rejected()
    {
        BarRules.Check(new Bar(Minute + 1, 1, 1, 1, 1, 1), out _).ShouldBe(BarVerdict.NotAWholeMinute);
    }
}
