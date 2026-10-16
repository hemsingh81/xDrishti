using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples (numbers written here, worked out by hand) for sma, ema and the rolling extremes.</summary>
public sealed class MovingAverageTests
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    private static List<double> Run(IIndicator indicator, IEnumerable<XDrishti.Domain.Market.Bars.Bar> bars)
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
    public void Sma_of_three_is_the_mean_of_the_last_three_closes_from_the_third_bar()
    {
        var values = Run(new Sma(3), Minutes.Closes(Day, 10, 12, 14, 13, 11));

        values[0].ShouldBeNaN();
        values[1].ShouldBeNaN();
        values[2].ShouldBe(12.0);
        values[3].ShouldBe(13.0);   // (12 + 14 + 13) / 3
        values[4].ShouldBe(38.0 / 3, 1e-12);   // (14 + 13 + 11) / 3
    }

    [Fact]
    public void Sma_reads_the_chosen_price_of_the_bar()
    {
        var bars = new[]
        {
            Minutes.Bar(Day, Minutes.Open, 10, 20, 5, 15),
            Minutes.Bar(Day, Minutes.Open + 1, 11, 30, 6, 16),
        };

        Run(new Sma(2, PriceSource.High), bars)[1].ShouldBe(25.0);
        Run(new Sma(2, PriceSource.Low), bars)[1].ShouldBe(5.5);
        Run(new Sma(2, PriceSource.Open), bars)[1].ShouldBe(10.5);
        Run(new Sma(2), bars)[1].ShouldBe(15.5);
    }

    [Fact]
    public void Sma_of_one_is_the_price_itself_and_is_ready_at_once()
    {
        var sma = new Sma(1);

        sma.Update(Minutes.Close(Day, Minutes.Open, 42));

        sma.IsReady.ShouldBeTrue();
        sma.Value.ShouldBe(42.0);
        sma.WarmUp.ShouldBe(1);
    }

    [Fact]
    public void Sma_does_not_drift_after_a_million_steps_of_large_and_small_values()
    {
        var sma = new Sma(5);
        for (var i = 0; i < 1_000_000; i++)
        {
            sma.Update(Minutes.Close(Day, Minutes.Open, i % 2 == 0 ? 1e9 : 0.1));
        }

        for (var i = 0; i < 5; i++)
        {
            sma.Update(Minutes.Close(Day, Minutes.Open, 7));
        }

        sma.Value.ShouldBe(7.0, 1e-9);
    }

    [Fact]
    public void Ema_is_seeded_with_the_mean_of_the_first_n_closes_then_smoothed_with_alpha_two_over_n_plus_one()
    {
        var values = Run(new Ema(3), Minutes.Closes(Day, 10, 12, 14, 13, 11));

        values[1].ShouldBeNaN();
        values[2].ShouldBe(12.0);     // (10 + 12 + 14) / 3
        values[3].ShouldBe(12.5);     // alpha 0.5: 0.5 * 13 + 0.5 * 12
        values[4].ShouldBe(11.75);    // 0.5 * 11 + 0.5 * 12.5
    }

    [Fact]
    public void Ema_of_one_follows_the_price()
    {
        var values = Run(new Ema(1), Minutes.Closes(Day, 5, 9, 7));

        values.ShouldBe([5.0, 9.0, 7.0]);
    }

    [Fact]
    public void Highest_and_lowest_include_the_current_bar_and_forget_bars_older_than_n()
    {
        var highs = new[] { 5.0, 3, 4, 2, 1, 6 };
        var bars = highs.Select((h, i) => Minutes.Bar(Day, Minutes.Open + i, h, h, 10 - h, h)).ToList();

        var highest = Run(new Highest(3), bars);
        var lowest = Run(new Lowest(3), bars);

        highest[1].ShouldBeNaN();
        highest.Skip(2).ShouldBe([5.0, 4, 4, 6]);   // windows (5,3,4) (3,4,2) (4,2,1) (2,1,6)
        lowest.Skip(2).ShouldBe([5.0, 6, 6, 4]);     // lows 5,7,6,8,9,4: (5,7,6) (7,6,8) (6,8,9) (8,9,4)
    }

    [Fact]
    public void Highest_with_equal_values_keeps_the_newest_and_window_of_one_is_the_bar()
    {
        var bars = new[] { 3.0, 3, 3, 2 }.Select((h, i) => Minutes.Bar(Day, Minutes.Open + i, h, h, h, h)).ToList();

        Run(new Highest(2), bars).Skip(1).ShouldBe([3.0, 3, 3]);
        Run(new Highest(1), bars).ShouldBe([3.0, 3, 3, 2]);
    }

    [Fact]
    public void Highest_reads_the_chosen_source()
    {
        var bars = new[] { Minutes.Bar(Day, Minutes.Open, 10, 20, 5, 15), Minutes.Bar(Day, Minutes.Open + 1, 12, 18, 4, 11) };

        Run(new Highest(2, PriceSource.Close), bars)[1].ShouldBe(15.0);
        Run(new Highest(2, PriceSource.Open), bars)[1].ShouldBe(12.0);
        Run(new Lowest(2, PriceSource.Open), bars)[1].ShouldBe(10.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_period_below_one_is_refused(int period)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Sma(period));
        Should.Throw<ArgumentOutOfRangeException>(() => new Ema(period));
        Should.Throw<ArgumentOutOfRangeException>(() => new Highest(period));
        Should.Throw<ArgumentOutOfRangeException>(() => new Lowest(period));
    }
}
