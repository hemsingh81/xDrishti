using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading.Pairing;

/// <summary>Spec P11-03 criteria 1 to 3 and 7: the pair split, the shortfall statistics against the hand-worked ten pairs and brute force, the verdict boundaries and the histogram edges.</summary>
public sealed class PairingTests
{
    private static readonly double[] Ten = [0.02, 0.05, -0.01, 0.08, 0.03, 0.00, 0.06, 0.04, 0.01, 0.02];

    [Fact]
    public void The_pair_splits_exactly_into_entry_and_exit()
    {
        var p = PairedShortfall.Pair(true, 1250.475m, 1262.00m, 1250.30m, 1263.00m, 10m).ShouldNotBeNull();

        p.PaperR.ShouldBe(1.2700, 1e-12);
        p.LiveR.ShouldBe(1.1525, 1e-12);
        p.EntryR.ShouldBe(0.0175, 1e-12);
        p.ExitR.ShouldBe(0.1000, 1e-12);
        p.ShortfallR.ShouldBe(0.1175, 1e-12);
        p.ShortfallR.ShouldBe(p.PaperR - p.LiveR, 1e-12);
    }

    [Fact]
    public void A_sell_flips_the_sign_and_a_zero_rho_is_no_pair()
    {
        var p = PairedShortfall.Pair(false, 100.00m, 99.00m, 100.10m, 98.80m, 2m).ShouldNotBeNull();
        p.ShortfallR.ShouldBe(p.PaperR - p.LiveR, 1e-12);
        p.EntryR.ShouldBe(0.05, 1e-12); // sold lower than paper: worse, positive
        PairedShortfall.Pair(true, 1m, 2m, 1m, 2m, 0m).ShouldBeNull();
    }

    [Fact]
    public void Ten_pairs_give_the_hand_worked_interval_but_no_verdict()
    {
        var s = ShortfallStats.Summarise(Ten, 0.10, 30).ShouldNotBeNull();

        s.Mean.ShouldBe(0.0300, 1e-9);
        s.Sd.ShouldBe(0.0279, 5e-5);
        s.Se.ShouldBe(0.00882, 5e-6);
        s.Lo95.ShouldBe(0.0101, 5e-5);
        s.Hi95.ShouldBe(0.0499, 5e-5);
        s.UpperOneSided.ShouldBe(0.0462, 5e-5);
        s.Verdict.ShouldBe(NonInferiority.Insufficient);
    }

    [Fact]
    public void The_statistics_match_a_brute_force_computation_on_random_data()
    {
        var rng = new Random(7);
        var v = Enumerable.Range(0, 40).Select(_ => (rng.NextDouble() * 0.4) - 0.1).ToArray();
        var s = ShortfallStats.Summarise(v, 0.10, 30).ShouldNotBeNull();
        var mean = v.Average();
        var sd = Math.Sqrt(v.Select(x => Math.Pow(x - mean, 2)).Sum() / 39);

        s.Mean.ShouldBe(mean, 1e-12);
        s.Sd.ShouldBe(sd, 1e-12);
        s.Hi95.ShouldBe(mean + (2.023 * sd / Math.Sqrt(40)), 1e-12); // df 39 row
        s.UpperOneSided.ShouldBe(mean + (1.685 * sd / Math.Sqrt(40)), 1e-12);
    }

    [Fact]
    public void Fewer_than_ten_pairs_or_a_non_finite_value_give_no_summary() =>
        (ShortfallStats.Summarise([.. Ten.Take(9)], 0.10, 30) is null && ShortfallStats.Summarise([.. Ten.Take(9), double.NaN], 0.10, 30) is null).ShouldBeTrue();

    [Theory]
    [InlineData(30, 0.10, 0.02, "within")] // upper exactly the margin
    [InlineData(30, 0.0999, 0.02, "within")]
    [InlineData(30, 0.1001, 0.02, "inconclusive")]
    [InlineData(30, 0.30, 0.10, "inconclusive")] // lower exactly the margin is not above it
    [InlineData(30, 0.30, 0.1001, "not_within")]
    [InlineData(29, 0.0, -0.1, "insufficient")]
    public void The_verdict_follows_the_one_sided_bounds(int n, double upper, double lower, string expected) =>
        NonInferiority.Verdict(n, upper, lower, 0.10, 30).ShouldBe(expected);

    [Fact]
    public void Histogram_edges_are_inclusive_on_the_lower_side_and_every_bin_is_drawn()
    {
        double[] edges = [-10, -5, -2, 0, 2, 5, 10, 20];
        var values = edges.Concat([-10.01, 25, 0.5, 1, 3, 7]).ToList();
        var bins = SlippageHistogram.Build(values).ShouldNotBeNull();

        bins.Count.ShouldBe(9);
        bins[0].Count.ShouldBe(1); // only -10.01 is below -10
        bins[1].Count.ShouldBe(1); // -10
        bins[8].Count.ShouldBe(2); // 20 and 25
        bins[4].Count.ShouldBe(3); // 0, 0.5, 1
        bins.Sum(b => b.Count).ShouldBe(values.Count);
        bins.Any(b => b.Count == 0).ShouldBeFalse();
        SlippageHistogram.Build([.. Enumerable.Repeat(1.0, 9)]).ShouldBeNull();
        SlippageHistogram.Build([.. Enumerable.Repeat(1.0, 10)])!.Count(b => b.Count == 0).ShouldBe(8); // a zero bin is a zero, not hidden
    }

    [Fact]
    public void The_pair_order_does_not_change_the_statistics()
    {
        var a = ShortfallStats.Summarise(Ten, 0.10, 10).ShouldNotBeNull();
        var b = ShortfallStats.Summarise([.. Ten.Reverse()], 0.10, 10).ShouldNotBeNull();
        a.Mean.ShouldBe(b.Mean, 1e-12);
        a.Sd.ShouldBe(b.Sd, 1e-12);
    }
}
