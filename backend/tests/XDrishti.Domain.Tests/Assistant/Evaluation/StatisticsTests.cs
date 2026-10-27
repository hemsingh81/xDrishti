namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 D3 and the test plan: the Wilson values, the exact sign test, the small-sample line.</summary>
public sealed class StatisticsTests
{
    [Theory]
    [InlineData(27, 30, 0.744, 0.965)]
    [InlineData(10, 10, 0.722, 1.0)]
    [InlineData(160, 200, 0.739, 0.850)]
    public void Wilson_intervals_equal_the_published_values(int k, int n, double low, double high)
    {
        var (l, h) = Wilson.Interval(k, n);

        l.ShouldBe(low, 0.001);
        h.ShouldBe(high, 0.001);
    }

    [Fact]
    public void Wilson_of_nothing_is_the_whole_range_and_never_leaves_it()
    {
        Wilson.Interval(0, 0).ShouldBe((0, 1));
        var (low, high) = Wilson.Interval(0, 5);
        low.ShouldBe(0);
        high.ShouldBeInRange(0, 1);
    }

    [Fact]
    public void Thirty_cases_cannot_separate_twenty_seven_from_twenty_four()
    {
        Wilson.Interval(24, 30).High.ShouldBeGreaterThan(Wilson.Interval(27, 30).Low);
        EvalTables.Thresholds().SmallSampleLine().ShouldBe("30 cases cannot separate 27 from 24 correct");
    }

    [Theory]
    [InlineData(6, 0, 0.03125)]
    [InlineData(0, 6, 0.03125)]
    [InlineData(5, 0, 0.0625)]
    [InlineData(3, 3, 1.0)]
    [InlineData(0, 0, 1.0)]
    [InlineData(2, 1, 1.0)]
    [InlineData(1, 8, 0.0390625)]
    public void The_sign_test_is_exact_and_two_sided(int b, int c, double p) => PairedComparison.PValue(b, c).ShouldBe(p, 1e-12);

    [Fact]
    public void Six_discordant_pairs_all_one_way_are_the_fewest_for_five_percent()
    {
        PairedComparison.Needed().ShouldBe(6);
        PairedComparison.NeededSentence().ShouldBe("with this many cases at least 6 discordant pairs, all one way, are needed for p < 0.05");
    }

    [Fact]
    public void Compare_counts_pairs_and_refuses_unpaired_runs()
    {
        bool[] before = [true, true, false, false, true, true];
        bool[] now = [true, false, false, true, false, false];

        var result = PairedComparison.Compare(before, now).Value;

        (result.BothPass, result.BothFail, result.B, result.C).ShouldBe((1, 1, 3, 1));
        result.Discordant.ShouldBe(4);
        PairedComparison.Compare(before, [true]).Error.Code.ShouldBe("eval.not_paired");
    }
}
