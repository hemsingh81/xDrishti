using XDrishti.Indicators.Tests.Support;
using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for macd_line, macd_signal and macd_hist: both EMAs seeded with their simple mean, the signal seeded with the mean of the line's first values.</summary>
public sealed class MacdTests
{
    private static readonly double[] Closes = [10, 12, 11, 13, 15, 14];

    private static List<double> Values(IIndicator indicator) => Run(indicator, Minutes.Closes(Day, Closes));

    [Fact]
    public void The_line_is_the_fast_ema_minus_the_slow_ema_and_ready_at_the_slow_period()
    {
        // fast 2: seed (10 + 12)/2 = 11 at bar 2, then 2/3 x + 1/3 ema -> 11, 37/3, 127/9, 379/27
        // slow 3: seed (10 + 12 + 11)/3 = 11 at bar 3, then 1/2 x + 1/2 ema -> 12, 13.5, 13.75
        var line = Values(new MacdLine(2, 3, 2));

        line.Take(2).ShouldAllBe(v => double.IsNaN(v));
        line[2].ShouldBe(0.0, 1e-12);
        line[3].ShouldBe(1.0 / 3, 1e-12);
        line[4].ShouldBe(11.0 / 18, 1e-12);
        line[5].ShouldBe(31.0 / 108, 1e-12);
    }

    [Fact]
    public void The_signal_is_seeded_with_the_mean_of_the_first_g_line_values_not_with_the_first_one()
    {
        // line values from bar 3: 0, 1/3, 11/18, 31/108; g = 2 -> seed (0 + 1/3)/2 = 1/6 at bar 4, then 2/3 line + 1/3 signal
        var signal = Values(new MacdSignal(2, 3, 2));

        signal.Take(3).ShouldAllBe(v => double.IsNaN(v));
        signal[3].ShouldBe(1.0 / 6, 1e-12);
        signal[4].ShouldBe(25.0 / 54, 1e-12);
        signal[5].ShouldBe(28.0 / 81, 1e-12);
    }

    [Fact]
    public void The_histogram_is_the_line_minus_the_signal_from_the_bar_the_signal_is_ready()
    {
        var hist = Values(new MacdHist(2, 3, 2));

        hist.Take(3).ShouldAllBe(v => double.IsNaN(v));
        hist[3].ShouldBe(1.0 / 6, 1e-12);       // 1/3 - 1/6
        hist[4].ShouldBe(4.0 / 27, 1e-12);      // 11/18 - 25/54
        hist[5].ShouldBe(-19.0 / 324, 1e-12);   // 31/108 - 28/81
    }

    [Fact]
    public void A_signal_period_of_one_makes_the_signal_the_line_and_the_histogram_zero()
    {
        var line = Values(new MacdLine(2, 3, 1));
        var signal = Values(new MacdSignal(2, 3, 1));
        var hist = Values(new MacdHist(2, 3, 1));

        signal[2].ShouldBe(line[2], 1e-12);
        signal[5].ShouldBe(line[5], 1e-12);
        hist[2].ShouldBe(0.0, 1e-12);
        hist[5].ShouldBe(0.0, 1e-12);
        new MacdSignal(2, 3, 1).WarmUp.ShouldBe(3);
    }

    [Fact]
    public void Warm_up_is_slow_for_the_line_and_slow_plus_signal_minus_one_for_the_other_two()
    {
        new MacdLine(12, 26, 9).WarmUp.ShouldBe(26);
        new MacdSignal(12, 26, 9).WarmUp.ShouldBe(34);
        new MacdHist(12, 26, 9).WarmUp.ShouldBe(34);
    }

    [Fact]
    public void The_fast_period_must_be_below_the_slow_period()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MacdLine(26, 26, 9));
        Should.Throw<ArgumentOutOfRangeException>(() => new MacdSignal(30, 26, 9));
        Should.Throw<ArgumentOutOfRangeException>(() => new MacdHist(12, 26, 0));
    }

    [Fact]
    public void The_chosen_source_is_read()
    {
        var bars = Candles((10, 20, 5, 15), (10, 20, 5, 15), (10, 22, 5, 15));

        // highs 20, 20, 22: ema(1) is the value itself; ema(2) is seeded with 20 at bar 2, then 2/3 * 22 + 1/3 * 20 = 64/3
        var line = Run(new MacdLine(1, 2, 1, PriceSource.High), bars);

        line[1].ShouldBe(0.0, 1e-12);
        line[2].ShouldBe(2.0 / 3, 1e-12);
        Run(new MacdLine(1, 2, 1, PriceSource.Low), bars)[2].ShouldBe(0.0, 1e-12);
    }
}
