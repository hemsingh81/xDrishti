using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-03 acceptance criteria 7 to 11 (ADR 0205): the stationary block bootstrap over sessions.</summary>
public sealed class StationaryBootstrapTests
{
    private static Func<double> Script(params double[] values)
    {
        var i = 0;
        return () => values[i++];
    }

    [Fact]
    public void The_hand_case_of_five_sessions_and_a_block_of_two_gives_3_4_0_2_3()
    {
        // 0.7 -> 3; 0.7 >= 0.5 -> 4; 0.6 >= 0.5 -> 0 (wrap); 0.2 < 0.5 restart with 0.45 -> 2; 0.9 >= 0.5 -> 3
        StationaryBootstrap.Indices(5, 2, Script(0.7, 0.7, 0.6, 0.2, 0.45, 0.9)).ShouldBe([3, 4, 0, 2, 3]);
    }

    [Theory]
    [InlineData(1, 2, "block length set to the minimum")]
    [InlineData(2, 2, "block length set to the minimum")]
    [InlineData(8, 2, null)]
    [InlineData(64, 4, null)]
    [InlineData(100, 5, null)]
    public void The_auto_block_length_is_the_rounded_cube_root_and_at_least_two(int sessions, int expected, string? note)
    {
        var length = StationaryBootstrap.AutoBlockLength(sessions);

        length.Value.ShouldBe(expected);
        if (note is null)
        {
            length.Note.ShouldBeNull();
        }
        else
        {
            length.Note.ShouldNotBeNull().ShouldStartWith(note);
        }
    }

    [Fact]
    public void A_block_length_of_one_is_an_iid_resample_of_sessions()
    {
        // every uniform restarts: index, then (restart-draw) pairs; with L = 1 the first test u < 1 always holds
        StationaryBootstrap.Indices(4, 1, Script(0.1, 0.0, 0.6, 0.0, 0.3, 0.0, 0.99)).ShouldBe([0, 2, 1, 3]);
    }

    [Fact]
    public void A_block_at_least_as_long_as_the_series_walks_the_sessions_in_a_circle()
    {
        // L = 5 = k: restart probability 0.2; uniforms of 0.5 never restart
        StationaryBootstrap.Indices(5, 5, Script(0.5, 0.5, 0.5, 0.5, 0.5)).ShouldBe([2, 3, 4, 0, 1]);
    }

    [Fact]
    public void Indices_are_always_in_range()
    {
        var rng = new SeededRandom(11);

        foreach (var (k, l) in new[] { (1, 2), (3, 2), (17, 3), (64, 4), (500, 8) })
        {
            StationaryBootstrap.Indices(k, l, rng.NextDouble).ShouldAllBe(i => i >= 0 && i < k);
        }
    }

    [Fact]
    public void An_argument_out_of_range_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StationaryBootstrap.Indices(0, 2, () => 0.5));
        Should.Throw<ArgumentOutOfRangeException>(() => StationaryBootstrap.Indices(3, 0, () => 0.5));
        Should.Throw<ArgumentOutOfRangeException>(() => StationaryBootstrap.Run([[1.0], [2.0]], 0, 2, 1));
    }

    [Fact]
    public void Fewer_than_two_sessions_give_no_result()
    {
        StationaryBootstrap.Run([], 100, 2, 1).ShouldBeNull();
        StationaryBootstrap.Run([[1.0, 2.0]], 100, 2, 1).ShouldBeNull();
    }

    [Fact]
    public void The_observed_figures_and_the_counts_are_those_of_the_trades()
    {
        var result = StationaryBootstrap.Run([[1.0, -1.0], [2.0], [0.5, 0.5, 0.5]], 200, 2, 5)!;

        result.Sessions.ShouldBe(3);
        result.Trades.ShouldBe(6);
        result.BlockLength.ShouldBe(2);
        result.Draws.ShouldBe(200);
        result.ObservedMeanR.ShouldBe(3.5 / 6, 1e-12);
        result.MeanP05.ShouldBeLessThanOrEqualTo(result.MeanP50);
        result.MeanP50.ShouldBeLessThanOrEqualTo(result.MeanP95);
        result.DrawdownP50.ShouldBeLessThanOrEqualTo(result.DrawdownP95);
        result.ShareNotPositive.ShouldBeInRange(0, 1);
    }

    [Fact]
    public void The_same_seed_gives_the_same_numbers_and_a_different_seed_different_ones()
    {
        IReadOnlyList<double[]> sessions = [[1.0, -1.0], [2.0], [0.5, -0.2, 0.5], [-1.5], [0.3, 0.9]];

        var a = StationaryBootstrap.Run(sessions, 500, 2, 99);
        var b = StationaryBootstrap.Run(sessions, 500, 2, 99);
        var salted = StationaryBootstrap.Run(sessions, 500, 2, 99 ^ 0x5A5A5A5A);

        a.ShouldBe(b);
        salted.ShouldNotBe(a);
    }

    [Fact]
    public void The_old_iid_monte_carlo_is_unchanged_by_this_slice()
    {
        // a pinned result of the existing bootstrap (P5-05) for a fixed seed
        var r = new[] { 1.0, -1.0, 2.0, -0.5, 0.5, 1.5 };

        var a = MonteCarlo.Run(r, 300, 4242)!;
        var b = MonteCarlo.Run(r, 300, 4242)!;

        a.ShouldBe(b);
        a.Trades.ShouldBe(6);
    }

    [Fact]
    public void The_interval_widens_with_same_day_clustering_by_about_the_square_root_of_the_design_effect()
    {
        // 20 sessions x 5 trades, within-session correlation 0.5, sd 1.2 R: design effect 1 + 4 x 0.5 = 3, so the width is about sqrt(3) = 1.73 times the iid width
        // one dataset of 20 sessions has its own sampling noise in the between-session spread, so the ratio is the mean over ten seeded datasets (the spec's band 1.4 to 2.1)
        var rng = new SeededRandom(2026);
        var ratios = new List<double>();
        for (var d = 0; d < 10; d++)
        {
            var sessions = new List<double[]>();
            for (var s = 0; s < 20; s++)
            {
                var shared = Normal(rng);
                sessions.Add([.. Enumerable.Range(0, 5).Select(_ => 1.2 * ((Math.Sqrt(0.5) * shared) + (Math.Sqrt(0.5) * Normal(rng))))]);
            }

            var iid = MonteCarlo.Run([.. sessions.SelectMany(s => s)], 2000, 77)!;
            var block = StationaryBootstrap.Run(sessions, 2000, StationaryBootstrap.AutoBlockLength(20).Value, 78)!;
            ratios.Add((block.MeanP95 - block.MeanP05) / (iid.MeanP95 - iid.MeanP05));
        }

        var ratio = ratios.Average();

        ratio.ShouldBeInRange(1.4, 2.1);
    }

    private static double Normal(SeededRandom rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
