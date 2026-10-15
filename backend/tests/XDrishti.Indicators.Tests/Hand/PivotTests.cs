using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for pivot_high/pivot_low and their ages: strict on both sides, confirmed n bars late, equal values are not pivots.</summary>
public sealed class PivotTests
{
    // highs by bar index 0..11 (n = 2): bar 2 (5) is a pivot, confirmed at index 4; bars 5 and 6 (4, 4) are equal and not; bar 9 (6) is a pivot, confirmed at index 11
    private static readonly double[] Highs = [1, 2, 5, 3, 2, 4, 4, 1, 0, 6, 2, 1];

    private static List<XDrishti.Domain.Market.Bars.Bar> HighBars() => Ranges([.. Highs.Select(h => (h, h - 0.5))]);

    // lows mirror the highs: bar 2 (5) is a pivot low, bars 5 and 6 are equal, bar 9 (4) is a pivot low
    private static readonly double[] Lows = [9, 8, 5, 7, 8, 6, 6, 9, 10, 4, 8, 9];

    private static List<XDrishti.Domain.Market.Bars.Bar> LowBars() => Ranges([.. Lows.Select(l => (l + 0.5, l))]);

    [Fact]
    public void A_pivot_high_is_known_only_when_the_nth_later_bar_closes_and_arrives_two_bars_late_for_n_equal_two()
    {
        var pivot = new PivotHigh(2);

        var values = Run(pivot, HighBars());

        // the peak at index 2 is not visible at index 2 or 3, only at index 4
        values.Take(4).ShouldAllBe(v => double.IsNaN(v));
        values[4].ShouldBe(5.0);
        values.Skip(4).Take(7).ShouldAllBe(v => v == 5.0, "equal highs 4 4 and the lower bars after are no pivots");
        values[11].ShouldBe(6.0);
        pivot.WarmUp.ShouldBe(5);
    }

    [Fact]
    public void The_age_is_n_when_the_pivot_is_confirmed_and_grows_by_one_per_bar_until_the_next_pivot()
    {
        var age = Run(new PivotHighAge(2), HighBars());

        age.Take(4).ShouldAllBe(v => double.IsNaN(v));
        age.Skip(4).Take(7).ShouldBe([2, 3, 4, 5, 6, 7, 8]);
        age[11].ShouldBe(2.0);
    }

    [Fact]
    public void A_pivot_low_mirrors_the_pivot_high_and_its_age_counts_the_same_way()
    {
        var low = Run(new PivotLow(2), LowBars());
        var age = Run(new PivotLowAge(2), LowBars());

        low.Take(4).ShouldAllBe(v => double.IsNaN(v));
        low[4].ShouldBe(5.0);
        low[10].ShouldBe(5.0);
        low[11].ShouldBe(4.0);
        age.Skip(4).Take(8).ShouldBe([2, 3, 4, 5, 6, 7, 8, 2]);
    }

    [Fact]
    public void Equal_highs_next_to_the_peak_make_it_no_pivot_and_a_tie_on_either_side_counts()
    {
        // peak 5 with an equal high one bar after: not strictly above -> no pivot
        Run(new PivotHigh(1), Ranges((1, 0), (5, 0), (5, 0), (1, 0), (0, 0))).ShouldAllBe(v => double.IsNaN(v));
        // an equal high one bar before
        Run(new PivotHigh(1), Ranges((5, 0), (5, 0), (1, 0), (0, 0))).ShouldAllBe(v => double.IsNaN(v));
        // a strict peak
        Run(new PivotHigh(1), Ranges((1, 0), (5, 0), (1, 0)))[2].ShouldBe(5.0);
    }

    [Fact]
    public void The_pivot_must_beat_all_n_bars_on_each_side_not_only_the_neighbours()
    {
        // n = 2: the peak 5 has a higher high 2 bars before -> no pivot at its confirmation
        Run(new PivotHigh(2), Ranges((6, 0), (1, 0), (5, 0), (1, 0), (0, 0))).ShouldAllBe(v => double.IsNaN(v));
        // and a higher high 2 bars after
        Run(new PivotHigh(2), Ranges((0, 0), (1, 0), (5, 0), (1, 0), (6, 0))).ShouldAllBe(v => double.IsNaN(v));
    }

    [Fact]
    public void The_value_at_a_bar_never_depends_on_later_bars()
    {
        var all = Run(new PivotHigh(2), HighBars());
        var upToConfirmation = Run(new PivotHigh(2), HighBars().Take(5));

        upToConfirmation.ShouldBe(all.Take(5).ToList());
        Run(new PivotHigh(2), HighBars().Take(4)).ShouldAllBe(v => double.IsNaN(v));
    }

    [Fact]
    public void A_pivot_period_of_zero_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PivotHigh(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new PivotLowAge(0));
    }
}
