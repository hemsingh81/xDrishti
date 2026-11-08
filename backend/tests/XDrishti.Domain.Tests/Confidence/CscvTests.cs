using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-03 part B, acceptance criteria 12 and 13: combinatorially symmetric cross-validation and PBO on hand tables, the refusals and the properties.</summary>
public sealed class CscvTests
{
    private static readonly double Third = Math.Log(1.0 / 3.0);

    private static readonly IReadOnlyList<double[]> HandCase =
    [
        [2, 1, -1, 0],
        [1, 0.5, 1, 1.5],
        [-2, 0, 3, 0.25],
    ];

    private static List<double[]> Noise(int variants, int days, ulong seed)
    {
        var random = new SeededRandom(seed);
        return [.. Enumerable.Range(0, variants).Select(_ => Enumerable.Range(0, days).Select(_ => random.NextDouble() - 0.5).ToArray())];
    }

    [Theory]
    [InlineData(4, 6)]
    [InlineData(6, 20)]
    [InlineData(8, 70)]
    [InlineData(10, 252)]
    [InlineData(16, 12870)]
    public void The_number_of_splits_is_the_central_binomial(int blocks, int splits) => Cscv.SplitCount(blocks).ShouldBe(splits);

    [Fact]
    public void Variants_tied_for_the_in_sample_best_share_the_pick_whatever_their_order()
    {
        // blocks 0 and 1 are a tie for A and B (mean 0.5), while A earns in block 2 and B does not: the pick must not depend on which comes first
        IReadOnlyList<double[]> tied = [[1, 0, 4, 0], [0, 1, 0, 0], [0.2, 0.2, 1, 1]];
        var forward = Cscv.Pbo(tied, 4);
        var reversed = Cscv.Pbo([tied[2], tied[1], tied[0]], 4);
        var rotated = Cscv.Pbo([tied[1], tied[2], tied[0]], 4);

        forward.Unavailable.ShouldBeNull();
        reversed.Pbo.ShouldBe(forward.Pbo);
        reversed.MedianLogit.ShouldBe(forward.MedianLogit);
        rotated.Pbo.ShouldBe(forward.Pbo);
        rotated.MedianLogit.ShouldBe(forward.MedianLogit);
    }

    [Fact]
    public void The_hand_case_of_three_variants_and_four_blocks_gives_four_sixths()
    {
        var result = Cscv.Pbo(HandCase, 4);

        result.Unavailable.ShouldBeNull();
        result.Splits.ShouldBe(6);
        result.Variants.ShouldBe(3);
        result.Pbo!.Value.ShouldBe(4.0 / 6.0, 1e-12);
        result.Pbo.Value.ShouldBeGreaterThan(0.5, "a PBO above one half is what the Gate flags");
        result.MedianLogit!.Value.ShouldBe(Third / 2, 1e-12, "the sorted logits are -1.0986 three times, 0, +1.0986 twice: the middle two are -1.0986 and 0");
    }

    [Fact]
    public void Twenty_eight_of_seventy_splits_at_or_below_zero_is_forty_percent_and_an_exact_zero_counts()
    {
        var logits = new List<double>();
        logits.AddRange(Enumerable.Repeat(-0.5, 20));
        logits.AddRange(Enumerable.Repeat(0.0, 8));
        logits.AddRange(Enumerable.Repeat(0.5, 42));

        var result = Cscv.FromLogits(logits, 17, 8);

        logits.Count.ShouldBe(Cscv.SplitCount(8));
        result.Pbo!.Value.ShouldBe(0.40, 1e-12);
        result.Splits.ShouldBe(70);
        result.MedianLogit!.Value.ShouldBe(0.5, 1e-12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(18)]
    public void An_odd_block_count_or_one_outside_four_to_sixteen_is_refused(int blocks)
    {
        var result = Cscv.Pbo(Noise(10, 64, 1), blocks);

        result.Pbo.ShouldBeNull();
        result.Splits.ShouldBe(0);
        result.Unavailable.ShouldNotBeNull().ShouldStartWith("unavailable: blocks");
    }

    [Fact]
    public void Identical_variants_are_refused_because_the_best_of_them_is_undefined()
    {
        double[] one = [1, -1, 0.5, 2, -2, 0.1, 0.3, -0.4];

        var result = Cscv.Pbo([one, [.. one], [.. one]], 4);

        result.Pbo.ShouldBeNull();
        result.Unavailable.ShouldNotBeNull().ShouldContain("identical");
    }

    [Fact]
    public void Series_that_do_not_cover_the_same_days_or_have_too_few_or_a_value_that_is_not_finite_are_refused()
    {
        Cscv.Pbo([[1, 2, 3, 4], [1, 2, 3]], 4).Unavailable.ShouldNotBeNull().ShouldContain("same days");
        Cscv.Pbo([[1, 2, 3], [3, 2, 1]], 4).Unavailable.ShouldNotBeNull().ShouldContain("fewer days than blocks");
        Cscv.Pbo([[1, 2, 3, double.NaN], [3, 2, 1, 0]], 4).Unavailable.ShouldNotBeNull().ShouldContain("not a finite number");
        Cscv.Pbo([[1, 2, 3, double.PositiveInfinity], [3, 2, 1, 0]], 4).Unavailable.ShouldNotBeNull().ShouldContain("not a finite number");
        Cscv.Pbo([[1, 2, 3, 4]], 4).Unavailable.ShouldNotBeNull().ShouldContain("at least 2 variants");
    }

    [Fact]
    public void Days_that_do_not_divide_by_the_blocks_drop_the_earliest_remainder()
    {
        var core = Noise(10, 64, 7);
        var withLead = core.Select(s => new[] { 9.0, -9.0, 3.0 }.Concat(s).ToArray()).ToList();

        Cscv.Pbo(withLead, 8).ShouldBe(Cscv.Pbo(core, 8));
    }

    [Fact]
    public void The_same_series_give_the_same_figure()
    {
        var a = Cscv.Pbo(Noise(12, 120, 3), 8);
        var b = Cscv.Pbo(Noise(12, 120, 3), 8);

        a.ShouldBe(b);
    }

    [Theory]
    [InlineData(1UL, PboMetric.Mean)]
    [InlineData(2UL, PboMetric.Sharpe)]
    [InlineData(3UL, PboMetric.Mean)]
    [InlineData(4UL, PboMetric.Sharpe)]
    public void PBO_of_noise_is_a_share_between_zero_and_one_with_every_split_counted(ulong seed, PboMetric metric)
    {
        var result = Cscv.Pbo(Noise(12, 128, seed), 8, metric);

        result.Pbo!.Value.ShouldBeInRange(0.0, 1.0);
        result.Splits.ShouldBe(70);
        (result.Pbo.Value * 70).ShouldBe(Math.Round(result.Pbo.Value * 70), 1e-9, "a whole number of splits");
    }

    [Fact]
    public void A_variant_better_in_every_block_wins_in_sample_and_ranks_top_out_of_sample_so_PBO_is_zero()
    {
        var series = Noise(10, 64, 11);
        series[3] = [.. series[3].Select(v => v + 5)];

        var result = Cscv.Pbo(series, 8);

        result.Pbo!.Value.ShouldBe(0.0);
        result.MedianLogit!.Value.ShouldBe(Math.Log(10.0 / 1.0), 1e-12, "rank 10 of 10: omega 10/11, logit ln(10)");
    }

    [Fact]
    public void A_variant_best_in_one_half_and_worst_in_the_other_is_the_overfit_winner_and_PBO_is_one()
    {
        // two variants, two halves: v0 wins the first two blocks and loses the last two by the same amount; v1 is the mirror image
        double[] v0 = [3, 3, -3, -3];
        double[] v1 = [-3, -3, 3, 3];

        var result = Cscv.Pbo([v0, v1], 4);

        // in the splits {1,2} and {3,4} the in-sample winner is last out of sample; in the other four splits the two tie in-sample (mean 0) and the first is taken, and tie out of sample (mean 0): the middle rank
        result.Pbo!.Value.ShouldBe(1.0, 1e-12);
    }

    [Fact]
    public void Scoring_by_Sharpe_ignores_the_scale_of_a_variant_and_scoring_by_the_mean_does_not()
    {
        var series = Noise(8, 96, 21);
        var scaled = series.Select((s, i) => i == 2 ? [.. s.Select(v => v * 100)] : s).ToList();

        Cscv.Pbo(scaled, 8, PboMetric.Sharpe).Pbo.ShouldBe(Cscv.Pbo(series, 8, PboMetric.Sharpe).Pbo);
        Cscv.Pbo(scaled, 8, PboMetric.Mean).Pbo.ShouldNotBe(Cscv.Pbo(series, 8, PboMetric.Mean).Pbo);
    }

    [Fact]
    public void A_flat_variant_scores_zero_by_Sharpe_and_does_not_break_the_ranking()
    {
        var series = Noise(6, 64, 5);
        series.Add(new double[64]);

        var result = Cscv.Pbo(series, 8, PboMetric.Sharpe);

        result.Unavailable.ShouldBeNull();
        result.Pbo!.Value.ShouldBeInRange(0.0, 1.0);
    }
}
