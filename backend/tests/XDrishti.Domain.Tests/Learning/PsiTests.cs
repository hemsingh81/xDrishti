using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10b (ADR 0215), spec AC8: the population stability index and its cuts.</summary>
public sealed class PsiTests
{
    private static readonly LadderOptions Options = new();

    [Fact]
    public void The_hand_worked_index_is_0_1695_and_reads_as_watch()
    {
        var psi = Psi.Index([0.5, 0.5], [0.7, 0.3]);

        psi.ShouldBe((0.2 * Math.Log(1.4)) + (-0.2 * Math.Log(0.6)), 1e-12);
        psi.ShouldBe(0.1695, 1e-4);
        Psi.Level(psi, Options).ShouldBe(PsiLevel.Watch);
    }

    [Fact]
    public void The_same_shares_give_zero()
    {
        Psi.Index([0.25, 0.25, 0.5], [0.25, 0.25, 0.5]).ShouldBe(0, 1e-15);
    }

    [Fact]
    public void An_empty_bin_is_lifted_to_the_floor_so_the_index_exists()
    {
        var psi = Psi.Index([0.5, 0.5], [1.0, 0.0]);

        double.IsFinite(psi).ShouldBeTrue();
        psi.ShouldBeGreaterThan(Options.PsiAlarm);
    }

    [Theory]
    [InlineData(0.0999, PsiLevel.Stable)]
    [InlineData(0.10, PsiLevel.Watch)]
    [InlineData(0.25, PsiLevel.Watch)]
    [InlineData(0.2501, PsiLevel.Alarm)]
    public void The_cuts_are_0_10_and_0_25(double psi, PsiLevel level)
    {
        Psi.Level(psi, Options).ShouldBe(level);
    }

    [Fact]
    public void Bad_shares_are_refused()
    {
        Should.Throw<ArgumentException>(() => Psi.Index([0.5], [0.5, 0.5]));
        Should.Throw<ArgumentException>(() => Psi.Index([0.5, double.NaN], [0.5, 0.5]));
        Should.Throw<ArgumentException>(() => Psi.Index([-0.1, 1.1], [0.5, 0.5]));
    }

    [Fact]
    public void Samples_are_binned_by_training_quantiles()
    {
        double[] train = [.. Enumerable.Range(0, 10).Select(i => (double)i)];
        double[] recent = [5, 6, 7, 8, 9, 5, 6, 7, 0, 1];

        // edge at 5: training shares (0.5, 0.5); recent below 5: two of ten, so (0.2, 0.8)
        var psi = Psi.Compute(train, recent, 2, 1)!.Value;

        psi.ShouldBe(Psi.Index([0.5, 0.5], [0.2, 0.8]), 1e-12);
        Psi.Compute(train, train, 2, 1)!.Value.ShouldBe(0, 1e-12);
    }

    [Fact]
    public void A_two_valued_feature_has_one_edge_and_no_empty_first_bin()
    {
        double[] train = [.. Enumerable.Range(0, 20).Select(i => (double)(i % 2))];
        double[] recent = [.. Enumerable.Range(0, 20).Select(i => i < 14 ? 1.0 : 0.0)];

        Psi.Compute(train, recent, 10, 1)!.Value.ShouldBe(Psi.Index([0.5, 0.5], [0.3, 0.7]), 1e-12);
    }

    [Fact]
    public void Non_finite_values_are_left_out()
    {
        double[] train = [.. Enumerable.Range(0, 10).Select(i => (double)i), double.NaN, double.PositiveInfinity];

        Psi.Compute(train, [.. train], 2, 1)!.Value.ShouldBe(0, 1e-12);
    }

    [Fact]
    public void Too_few_rows_or_a_constant_training_feature_is_unavailable_and_never_zero()
    {
        double[] train = [.. Enumerable.Range(0, 40).Select(i => (double)i)];

        Psi.Compute(train, [1, 2, 3], 10, 30).ShouldBeNull();
        Psi.Compute([1, 2, 3], train, 10, 30).ShouldBeNull();
        Psi.Compute([.. Enumerable.Repeat(3.0, 40)], train, 10, 30).ShouldBeNull();
    }

    [Fact]
    public void A_small_sample_uses_fewer_bins_so_that_noise_alone_does_not_ring_the_alarm()
    {
        // 90 test rows against 600 training rows from the same distribution: 10 bins would put the index near 0.1 by chance, 4 bins near 0.04
        var random = new Random(11);
        double[] train = [.. Enumerable.Range(0, 600).Select(_ => random.NextDouble())];
        var worst = 0.0;
        for (var rep = 0; rep < 40; rep++)
        {
            double[] recent = [.. Enumerable.Range(0, 90).Select(_ => random.NextDouble())];
            worst = Math.Max(worst, Psi.Compute(train, recent, 10, 30)!.Value);
        }

        worst.ShouldBeLessThan(0.25);
    }

    [Fact]
    public void A_two_valued_feature_is_read_whichever_value_is_the_minority()
    {
        double[] train = [.. Enumerable.Repeat(0.0, 80), .. Enumerable.Repeat(1.0, 20)];
        double[] same = [.. Enumerable.Repeat(0.0, 160), .. Enumerable.Repeat(1.0, 40)];
        double[] flipped = [.. Enumerable.Repeat(1.0, 80), .. Enumerable.Repeat(0.0, 20)];

        Psi.Compute(train, same, 4, 30)!.Value.ShouldBe(0, 1e-12);
        Psi.Compute(flipped, [.. Enumerable.Repeat(0.0, 50), .. Enumerable.Repeat(1.0, 50)], 4, 30).ShouldNotBeNull();
    }

    [Fact]
    public void A_jump_in_the_share_of_a_minority_value_from_20_to_50_percent_reads_alarm()
    {
        double[] train = [.. Enumerable.Repeat(0.0, 80), .. Enumerable.Repeat(1.0, 20)];
        double[] recent = [.. Enumerable.Repeat(0.0, 50), .. Enumerable.Repeat(1.0, 50)];

        var psi = Psi.Compute(train, recent, 4, 30)!.Value;

        psi.ShouldBe(Psi.Index([0.8, 0.2], [0.5, 0.5]), 1e-12);
        Psi.Level(psi, Options).ShouldBe(PsiLevel.Alarm);
    }

    [Fact]
    public void A_clear_shift_still_alarms_at_90_rows_with_the_coarser_bins()
    {
        var random = new Random(5);
        double[] train = [.. Enumerable.Range(0, 600).Select(_ => random.NextDouble())];
        double[] recent = [.. Enumerable.Range(0, 90).Select(_ => 0.5 + (0.5 * random.NextDouble()))]; // every test row in the top half of the training distribution

        Psi.Level(Psi.Compute(train, recent, 10, 30)!.Value, Options).ShouldBe(PsiLevel.Alarm);
    }
}
