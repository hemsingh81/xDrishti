using XDrishti.Indicators.Tests.Support;
using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for the Bollinger band features: the standard deviation is the population one (÷ n).</summary>
public sealed class BollingerTests
{
    private static readonly double Sigma = Math.Sqrt(8.0 / 3);

    private static List<double> Values(IIndicator indicator, params double[] closes) => Run(indicator, Minutes.Closes(Day, closes));

    [Fact]
    public void The_bands_use_the_population_standard_deviation_not_the_sample_one()
    {
        // window 2 4 6: mean 4, squared deviations 4 + 0 + 4 = 8; population variance 8/3 (sample variance would be 4, sigma 2)
        Values(new BbMiddle(3, 2), 2, 4, 6)[2].ShouldBe(4.0, 1e-12);
        Values(new BbUpper(3, 2), 2, 4, 6)[2].ShouldBe(4 + 2 * Sigma, 1e-12);
        Values(new BbLower(3, 2), 2, 4, 6)[2].ShouldBe(4 - 2 * Sigma, 1e-12);
        Math.Abs(Values(new BbUpper(3, 2), 2, 4, 6)[2] - 8.0).ShouldBeGreaterThan(1e-3);   // the sample deviation would give 8
        Values(new BbWidth(3, 2), 2, 4, 6)[2].ShouldBe(4 * Sigma / 4, 1e-12);   // (upper - lower) / middle
    }

    [Fact]
    public void The_bands_are_ready_at_bar_n_and_the_window_slides()
    {
        var upper = Values(new BbUpper(3, 1), 2, 4, 6, 8, 20);

        upper.Take(2).ShouldAllBe(v => double.IsNaN(v));
        upper[3].ShouldBe(6 + Sigma, 1e-12);                      // window 4 6 8
        // window 6 8 20: mean 34/3, deviations -16/3 -10/3 26/3 -> (256 + 100 + 676)/9 = 1032/9, variance /3
        upper[4].ShouldBe(34.0 / 3 + Math.Sqrt(1032.0 / 27), 1e-12);
    }

    [Fact]
    public void A_flat_window_has_zero_deviation_exactly_and_zero_width()
    {
        Values(new BbUpper(3, 2), 7, 7, 7)[2].ShouldBe(7.0);
        Values(new BbLower(3, 2), 7, 7, 7)[2].ShouldBe(7.0);
        Values(new BbWidth(3, 2), 7, 7, 7)[2].ShouldBe(0.0);
    }

    [Fact]
    public void The_width_is_not_ready_while_the_middle_is_zero()
    {
        var width = new BbWidth(2, 2);

        Values(width, -1, 1)[1].ShouldBeNaN();
        width.IsReady.ShouldBeFalse();
    }

    [Fact]
    public void The_band_factor_must_be_above_zero_and_finite()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BbUpper(3, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => new BbLower(3, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => new BbMiddle(3, double.NaN));
        Should.Throw<ArgumentOutOfRangeException>(() => new BbWidth(3, double.PositiveInfinity));
        Should.Throw<ArgumentOutOfRangeException>(() => new BbUpper(0, 2));
    }

    [Fact]
    public void The_source_is_read()
    {
        var bars = Candles((10, 20, 4, 15), (10, 22, 6, 15));

        Values2(new BbMiddle(2, 2, PriceSource.High), bars).ShouldBe(21.0);
        Values2(new BbMiddle(2, 2, PriceSource.Low), bars).ShouldBe(5.0);
    }

    private static double Values2(IIndicator indicator, List<XDrishti.Domain.Market.Bars.Bar> bars) => Run(indicator, bars)[^1];
}
