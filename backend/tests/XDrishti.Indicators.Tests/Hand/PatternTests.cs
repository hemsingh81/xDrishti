using XDrishti.Indicators.Tests.Support;
using static XDrishti.Indicators.Tests.Support.HandRun;

namespace XDrishti.Indicators.Tests.Hand;

/// <summary>Hand-calculated examples for inside_bar, nr(n) and engulfing.</summary>
public sealed class PatternTests
{
    [Fact]
    public void An_inside_bar_is_strictly_inside_the_previous_high_and_low()
    {
        var bars = Ranges((10, 5), (9, 6), (9, 6), (10, 5), (9.5, 5.5));
        var inside = new InsideBar();

        var values = Run(inside, bars);

        values[0].ShouldBeNaN();
        values.Skip(1).ShouldBe([1, 0, 0, 1]);   // 2: inside; 3: equal to the previous -> not inside; 4: wider; 5: inside
        inside.WarmUp.ShouldBe(2);
    }

    [Fact]
    public void An_outside_or_one_sided_bar_is_not_an_inside_bar()
    {
        Run(new InsideBar(), Ranges((10, 5), (9, 4)))[1].ShouldBe(0.0);     // lower low
        Run(new InsideBar(), Ranges((10, 5), (11, 6)))[1].ShouldBe(0.0);    // higher high
    }

    [Fact]
    public void Nr_is_one_only_when_the_range_is_strictly_the_smallest_of_the_last_n_and_ties_are_not_narrower()
    {
        // ranges 3 2 2 1 2
        var bars = Ranges((13, 10), (12, 10), (12, 10), (11, 10), (12, 10));
        var nr = new NarrowRange(3);

        var values = Run(nr, bars);

        values.Take(2).ShouldAllBe(v => double.IsNaN(v));
        values[2].ShouldBe(0.0);   // 2 against 3 and 2: a tie with the 2
        values[3].ShouldBe(1.0);   // 1 against 2 and 2
        values[4].ShouldBe(0.0);   // 2 against 2 and 1
        nr.WarmUp.ShouldBe(3);
    }

    [Fact]
    public void Nr_of_two_compares_with_the_previous_bar_only_and_needs_at_least_two()
    {
        var values = Run(new NarrowRange(2), Ranges((13, 10), (12, 10), (12, 10)));

        values[1].ShouldBe(1.0);
        values[2].ShouldBe(0.0);
        Should.Throw<ArgumentOutOfRangeException>(() => new NarrowRange(1));
    }

    [Fact]
    public void Engulfing_is_plus_one_for_a_bullish_and_minus_one_for_a_bearish_body_that_covers_the_previous_body()
    {
        var bars = Candles(
            (10, 10.5, 7.5, 8),      // 1: down body 10 -> 8
            (7.5, 11.5, 7, 11),      // 2: up, open 7.5 <= prev close 8, close 11 >= prev open 10 -> bullish
            (11.5, 12, 6.5, 7),      // 3: down, open 11.5 >= prev close 11, close 7 <= prev open 7.5 -> bearish
            (7, 8, 6, 7),            // 4: zero body -> 0
            (6, 9.5, 5.5, 9),        // 5: previous body is 0 -> 0
            (9.5, 12.5, 9, 12),      // 6: up after up (same colours) -> 0
            (13, 13.5, 8, 8.5),      // 7: down, open 13 >= prev close 12, close 8.5 <= prev open 9.5 -> bearish
            (8, 9, 7.5, 8.2));       // 8: up but close 8.2 < prev open 13 -> not covered -> 0
        var engulfing = new Engulfing();

        var values = Run(engulfing, bars);

        values[0].ShouldBeNaN();
        values.Skip(1).ShouldBe([1, -1, 0, 0, 0, -1, 0]);
        engulfing.WarmUp.ShouldBe(2);
    }

    [Fact]
    public void Engulfing_needs_the_open_inside_the_previous_body_edge_and_is_judged_on_bodies_not_ranges()
    {
        // previous down 10 -> 8; current up 8.5 -> 11: the open 8.5 is above the previous close 8 -> not engulfing
        Run(new Engulfing(), Candles((10, 10, 8, 8), (8.5, 11, 8.5, 11)))[1].ShouldBe(0.0);
        // the current range is huge but the body does not cover: still 0
        Run(new Engulfing(), Candles((10, 10, 8, 8), (8.5, 20, 1, 9)))[1].ShouldBe(0.0);
        // touching the edges is enough: open = prev close, close = prev open
        Run(new Engulfing(), Candles((10, 10, 8, 8), (8, 10, 8, 10)))[1].ShouldBe(1.0);
    }
}
