using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>The percentile-rank window behind <c>vix_pct</c> and <c>index_vol_pct</c>: <c>100 × (#below + 0.5 × #equal incl. itself) / size</c> over the last <c>size</c> values, NaN until the window is full.</summary>
public sealed class PercentileWindowTests
{
    private static List<double> Push(PercentileWindow window, params double[] values) => [.. values.Select(window.Push)];

    [Fact]
    public void The_rank_is_not_ready_until_the_window_is_full_and_then_every_value_has_one()
    {
        var ranks = Push(new PercentileWindow(3), 5, 6, 7, 8);

        ranks[0].ShouldBeNaN();
        ranks[1].ShouldBeNaN();
        ranks[2].ShouldBe(100.0 * 2.5 / 3);     // 7 is above 5 and 6
        ranks[3].ShouldBe(100.0 * 2.5 / 3);     // [6 7 8]: 8 is the highest
    }

    [Fact]
    public void The_lowest_value_ranks_half_a_place_the_highest_all_but_half_a_place_and_equal_values_fifty()
    {
        Push(new PercentileWindow(4), 9, 8, 7, 1)[3].ShouldBe(12.5);
        Push(new PercentileWindow(4), 1, 2, 3, 9)[3].ShouldBe(87.5);
        Push(new PercentileWindow(4), 5, 5, 5, 5)[3].ShouldBe(50);
        Push(new PercentileWindow(1), 42).ShouldBe([50]);
    }

    [Fact]
    public void Ties_take_the_middle_of_their_span()
    {
        // [20 20 10 20]: one below (10), three equal including the newest -> 100 * (1 + 1.5) / 4
        Push(new PercentileWindow(4), 15, 20, 20, 10, 20)[4].ShouldBe(62.5);
        // [20 10 20 15]: 15 has one below and itself
        Push(new PercentileWindow(4), 15, 20, 20, 10, 20, 15)[5].ShouldBe(37.5);
    }

    [Fact]
    public void The_window_slides_so_a_value_that_left_it_no_longer_counts()
    {
        // [1 9 9]: one below, two equal -> 100 * (1 + 1) / 3; one push later the 1 has left: [9 9 9] -> all equal -> 50
        var ranks = Push(new PercentileWindow(3), 1, 9, 9, 9, 9);

        ranks[2].ShouldBe(100.0 * 2 / 3);
        ranks[3].ShouldBe(50);
        ranks[4].ShouldBe(50);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(252)]
    public void A_plain_recount_of_every_window_gives_the_same_ranks_with_many_ties(int size)
    {
        var random = new Random(size);
        var values = Enumerable.Range(0, 1_500).Select(_ => (double)random.Next(0, 12)).ToArray();
        var window = new PercentileWindow(size);

        for (var i = 0; i < values.Length; i++)
        {
            var rank = window.Push(values[i]);
            if (i + 1 < size)
            {
                rank.ShouldBeNaN();
                continue;
            }

            var inWindow = values.Skip(i + 1 - size).Take(size).ToArray();
            var expected = 100.0 * (inWindow.Count(v => v < values[i]) + 0.5 * inWindow.Count(v => v == values[i])) / size;
            Tolerance.Identical(rank, expected).ShouldBeTrue($"size {size}, value {i}: {rank:R} vs {expected:R}");
        }
    }

    [Fact]
    public void A_window_needs_at_least_one_place()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PercentileWindow(0));
    }
}
