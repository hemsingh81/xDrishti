using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criterion 4 and the definitions of the intervals: t table, order-statistic ranks against a brute-force binomial.</summary>
public sealed class SlippageStatsTests
{
    private static readonly double[] Ten = [2.0, 3.5, 1.0, 5.0, -1.0, 4.0, 2.5, 6.0, 0.5, 3.0];

    [Fact]
    public void The_ten_observation_case_gives_the_spec_numbers()
    {
        var s = SlippageStats.Summarise(Ten).ShouldNotBeNull();
        s.N.ShouldBe(10);
        s.Mean.ShouldBe(2.65, 1e-9);
        s.Median.ShouldBe(2.75, 1e-9);
        s.StandardDeviation.ShouldBe(2.1220, 1e-3);
        s.MeanLo.ShouldBe(1.13, 0.01);
        s.MeanHi.ShouldBe(4.17, 0.01);
        s.MedianLo.ShouldBe(0.5);
        s.MedianHi.ShouldBe(5.0);
        s.MedianCoverage.ShouldNotBeNull().ShouldBe(0.979, 0.001);
    }

    [Fact]
    public void Fewer_than_six_or_a_non_finite_value_gets_no_summary()
    {
        SlippageStats.Summarise([1, 2, 3, 4, 5]).ShouldBeNull();
        SlippageStats.Summarise([1, 2, 3, 4, 5, double.NaN]).ShouldBeNull();
    }

    [Theory]
    [InlineData(5, 2.571)]
    [InlineData(8, 2.306)]
    [InlineData(9, 2.262)]
    [InlineData(13, 2.262)]
    [InlineData(14, 2.145)]
    [InlineData(18, 2.145)]
    [InlineData(19, 2.093)]
    [InlineData(29, 2.045)]
    [InlineData(39, 2.023)]
    [InlineData(59, 2.001)]
    [InlineData(60, 2.000)]
    [InlineData(119, 2.000)]
    [InlineData(120, 1.96)]
    [InlineData(5000, 1.96)]
    public void The_t_table_uses_the_nearest_lower_row(int df, double expected) => StudentT.Quantile95(df).ShouldBe(expected);

    [Fact]
    public void A_sample_under_six_has_no_t_row() => Should.Throw<ArgumentOutOfRangeException>(() => StudentT.Quantile95(4));

    [Theory]
    [InlineData(5, null)]
    [InlineData(6, 1)]
    [InlineData(10, 2)]
    [InlineData(20, 6)]
    [InlineData(30, 10)]
    public void The_median_rank_is_the_largest_with_a_tail_of_at_most_2_5_percent(int n, int? expected) => SlippageStats.MedianRank(n).ShouldBe(expected);

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void The_rank_matches_a_brute_force_binomial_sum(int n)
    {
        double Cdf(int m)
        {
            double sum = 0;
            for (var i = 0; i <= m; i++)
            {
                sum += Choose(n, i) / Math.Pow(2, n);
            }

            return sum;
        }

        int? brute = null;
        for (var k = 1; k <= n / 2; k++)
        {
            if (Cdf(k - 1) <= 0.025)
            {
                brute = k;
            }
        }

        SlippageStats.MedianRank(n).ShouldBe(brute);
        SlippageStats.BinomialCdf(n, 1).ShouldBe(Cdf(1), 1e-10);
    }

    [Fact]
    public void Twenty_observations_give_ranks_6_and_15_at_95_9_percent()
    {
        var values = Enumerable.Range(1, 20).Select(i => (double)i).ToList();
        var s = SlippageStats.Summarise(values).ShouldNotBeNull();
        s.MedianLo.ShouldBe(6);
        s.MedianHi.ShouldBe(15);
        s.MedianCoverage.ShouldNotBeNull().ShouldBe(0.959, 0.001);
    }

    private static double Choose(int n, int k)
    {
        double r = 1;
        for (var i = 1; i <= k; i++)
        {
            r = r * (n - k + i) / i;
        }

        return r;
    }
}
