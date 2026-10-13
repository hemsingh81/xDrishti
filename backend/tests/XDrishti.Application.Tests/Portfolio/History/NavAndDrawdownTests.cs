using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>The unit-value (NAV) series and the maximum drawdown (acceptance criterion 4 of P2-07).</summary>
public sealed class NavAndDrawdownTests
{
    private static (DateOnly Date, decimal Nav)[] Series(params decimal[] navs) => [.. navs.Select((n, i) => (Oct(5).AddDays(i), n))];

    [Fact]
    public void The_nav_starts_at_100_and_follows_the_value_when_nothing_is_put_in_or_taken_out()
    {
        var nav = NavSeries.Compute([0m, 1100m, 990m], [1000m, 0m, 0m], [false, false, false]);

        nav.ShouldBe([100m, 110m, 99m]);
    }

    [Fact]
    public void A_deposit_issues_units_at_the_nav_of_the_day_and_does_not_change_it()
    {
        // 10 units at 100; the price falls to 80 (value 800); 800 more is put in at that NAV (10 units); the price returns to 100 (value 2,000 / 20 units).
        var nav = NavSeries.Compute([0m, 800m, 2000m], [1000m, 800m, 0m], [false, false, false]);

        nav.ShouldBe([100m, 80m, 100m]);
    }

    [Fact]
    public void A_withdrawal_cancels_units_and_does_not_change_the_nav()
    {
        var nav = NavSeries.Compute([0m, 1200m, 550m], [1000m, -600m, 0m], [false, false, false]);

        nav.ShouldBe([100m, 120m, 110m]); // 10 units; 600 out at 120 cancels 5; the 5 units left are worth 550: 110
    }

    [Fact]
    public void When_nothing_is_held_the_nav_stays_where_it_was_and_carries_on_after_a_new_purchase()
    {
        var nav = NavSeries.Compute([0m, 1200m, 0m, 0m, 660m], [1000m, -1200m, 0m, 600m, 0m], [false, true, true, false, false]);

        nav.ShouldBe([100m, 120m, 120m, 120m, 132m]);
    }

    [Fact]
    public void A_flat_series_has_no_drawdown()
    {
        DrawdownCalculator.Max(Series(100m, 100m, 100m)).ShouldBe(new Drawdown(0m, null, null));
    }

    [Fact]
    public void A_strictly_rising_series_has_no_drawdown()
    {
        DrawdownCalculator.Max(Series(100m, 101m, 105m, 120m)).ShouldBe(new Drawdown(0m, null, null));
    }

    [Fact]
    public void No_points_have_no_drawdown_value()
    {
        DrawdownCalculator.Max([]).ShouldBeNull();
    }

    [Fact]
    public void The_drawdown_is_the_largest_peak_to_trough_fall_with_its_dates()
    {
        // peak 120 on the 7th, low 90 on the 8th: 25 %; the later fall from 130 to 117 is only 10 %
        var drawdown = DrawdownCalculator.Max(Series(100m, 110m, 120m, 90m, 100m, 130m, 117m));

        drawdown.ShouldBe(new Drawdown(25m, Oct(7), Oct(8)));
    }

    [Fact]
    public void The_trough_is_the_lowest_point_after_the_peak_not_the_first_dip()
    {
        var drawdown = DrawdownCalculator.Max(Series(100m, 90m, 95m, 70m, 80m));

        drawdown.ShouldBe(new Drawdown(30m, Oct(5), Oct(8)));
    }

    [Fact]
    public void A_fall_that_is_not_recovered_still_counts_to_the_end_of_the_series()
    {
        DrawdownCalculator.Max(Series(100m, 80m)).ShouldBe(new Drawdown(20m, Oct(5), Oct(6)));
    }

    [Fact]
    public void The_benchmark_drawdown_uses_the_same_method_and_is_null_without_a_benchmark()
    {
        var points = new[]
        {
            new HistoryPoint(Oct(5), 1000m, 1000m, 1000m, 100m, 100m),
            new HistoryPoint(Oct(6), 1000m, 1000m, 800m, 100m, 80m),
            new HistoryPoint(Oct(7), 1000m, 1000m, 1000m, 100m, 100m),
        };
        var without = points.Select(p => p with { BenchmarkValue = null, BenchmarkNav = null }).ToArray();

        DrawdownCalculator.OfBenchmark(points).ShouldBe(new Drawdown(20m, Oct(5), Oct(6)));
        DrawdownCalculator.OfPortfolio(points).ShouldBe(new Drawdown(0m, null, null));
        DrawdownCalculator.OfBenchmark(without).ShouldBeNull();
    }
}
