using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for rsi and atr (Wilder smoothing).</summary>
public sealed class WilderTests
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    private static List<double> Run(IIndicator indicator, IEnumerable<Bar> bars)
    {
        var values = new List<double>();
        foreach (var bar in bars)
        {
            indicator.Update(bar);
            values.Add(indicator.Value);
        }

        return values;
    }

    [Fact]
    public void Rsi_of_three_is_seeded_with_the_mean_of_the_first_three_changes_and_ready_at_bar_four()
    {
        // closes 10 11 10 12 11 -> changes +1 -1 +2 -1
        var values = Run(new Rsi(3), Minutes.Closes(Day, 10, 11, 10, 12, 11));

        values.Take(3).ShouldAllBe(v => double.IsNaN(v));
        // avg gain (1 + 0 + 2) / 3 = 1, avg loss (0 + 1 + 0) / 3 = 1/3, RS 3, RSI 100 - 100 / 4
        values[3].ShouldBe(75.0, 1e-12);
        // gain (1 * 2 + 0) / 3 = 2/3, loss (1/3 * 2 + 1) / 3 = 5/9, RS 1.2, RSI 100 - 100 / 2.2
        values[4].ShouldBe(600.0 / 11, 1e-12);
    }

    [Fact]
    public void Rsi_is_one_hundred_when_there_were_gains_and_no_losses()
    {
        var values = Run(new Rsi(3), Minutes.Closes(Day, 10, 11, 12, 13, 14));

        values[3].ShouldBe(100.0);
        values[4].ShouldBe(100.0);
    }

    [Fact]
    public void Rsi_is_fifty_when_nothing_moved_and_zero_when_there_were_only_losses()
    {
        Run(new Rsi(3), Minutes.Closes(Day, 10, 10, 10, 10, 10))[3].ShouldBe(50.0);
        Run(new Rsi(3), Minutes.Closes(Day, 14, 13, 12, 11, 10))[3].ShouldBe(0.0);
    }

    [Fact]
    public void Rsi_returns_to_fifty_only_while_both_averages_are_zero()
    {
        // up, then flat: the average gain stays above zero, so the level is 100 until a loss comes
        var values = Run(new Rsi(2), Minutes.Closes(Day, 10, 11, 12, 12, 12));

        values[2].ShouldBe(100.0);
        values[4].ShouldBe(100.0);
    }

    [Fact]
    public void Atr_of_one_is_high_minus_low_on_the_first_bar_which_has_no_previous_close()
    {
        var atr = new Atr(1);

        atr.Update(Minutes.Bar(Day, Minutes.Open, 100, 112, 104 - 4, 110));

        atr.IsReady.ShouldBeTrue();
        atr.Value.ShouldBe(12.0);   // 112 - 100, although the bar is far from any previous close
    }

    [Fact]
    public void Atr_of_three_seeds_with_the_mean_of_the_first_three_true_ranges_then_smooths()
    {
        var bars = new[]
        {
            Minutes.Bar(Day, Minutes.Open, 10, 12, 10, 11),       // TR 12 - 10 = 2
            Minutes.Bar(Day, Minutes.Open + 1, 11, 13, 11, 12),   // max(2, |13 - 11|, |11 - 11|) = 2
            Minutes.Bar(Day, Minutes.Open + 2, 12, 12, 9, 10),    // max(3, |12 - 12|, |9 - 12|) = 3
            Minutes.Bar(Day, Minutes.Open + 3, 10, 15, 10, 14),   // max(5, |15 - 10|, |10 - 10|) = 5
        };

        var values = Run(new Atr(3), bars);

        values[1].ShouldBeNaN();
        values[2].ShouldBe(7.0 / 3, 1e-12);          // (2 + 2 + 3) / 3
        values[3].ShouldBe(29.0 / 9, 1e-12);         // (7/3 * 2 + 5) / 3
    }

    [Fact]
    public void True_range_uses_the_distance_to_the_previous_close_when_the_bar_gaps()
    {
        var bars = new[]
        {
            Minutes.Bar(Day, Minutes.Open, 100, 101, 99, 100),
            Minutes.Bar(Day, Minutes.Open + 1, 110, 111, 109, 110),   // gaps up: |111 - 100| = 11 beats 111 - 109 = 2
        };

        Run(new Atr(2), bars)[1].ShouldBe((2.0 + 11.0) / 2);
    }
}
