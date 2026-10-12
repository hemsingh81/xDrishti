using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class SanityCheckerTests
{
    private static readonly int Open = Minute(9, 15);

    [Fact]
    public void Bars_that_obey_the_row_rules_pass()
    {
        var check = SanityChecker.Check(FullWindow());

        (check.Status, check.BarsChecked, check.BadBars).ShouldBe((CheckStatus.Pass, 1875, 0));
        check.Examples.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(100, 99, 98, 100, "the high is below the open or close")]
    [InlineData(100, 101, 100.5, 100, "the low is above the open, close or high")]
    [InlineData(100, 101, 99, 0, "a price is not positive")]
    [InlineData(-1, 101, 99, 100, "a price is not positive")]
    [InlineData(100, double.NaN, 99, 100, "a price is not a number")]
    [InlineData(100, double.PositiveInfinity, 99, 100, "a price is not a number")]
    public void Each_kind_of_bad_bar_fails_the_check_and_is_named(double open, double high, double low, double close, string problem)
    {
        var check = SanityChecker.Check([Of(Wed, Open, open, high, low, close)]);

        (check.Status, check.BadBars).ShouldBe((CheckStatus.Fail, 1));
        check.Examples.ShouldHaveSingleItem().Problem.ShouldBe(problem);
    }

    [Fact]
    public void A_negative_volume_fails()
    {
        var check = SanityChecker.Check([Of(Wed, Open, 100, 101, 99, 100, volume: -5)]);

        (check.Status, check.Examples.Single().Problem).ShouldBe((CheckStatus.Fail, "the volume is negative"));
    }

    [Fact]
    public void A_zero_volume_is_fine()
    {
        SanityChecker.Check([Of(Wed, Open, 100, 101, 99, 100, volume: 0)]).Status.ShouldBe(CheckStatus.Pass);
    }

    [Fact]
    public void The_examples_are_the_first_few_in_india_time_and_the_count_is_exact()
    {
        var bars = Enumerable.Range(0, 25).Select(i => Of(Wed, Open + i, 100, 99, 98, 100)).ToList();

        var check = SanityChecker.Check(bars);

        check.BadBars.ShouldBe(25);
        check.Examples.Count.ShouldBe(SanityChecker.MaxExamples);
        check.Examples[0].Ts.ShouldBe(new DateTimeOffset(2026, 10, 7, 9, 15, 0, TimeSpan.FromMinutes(330)));
        check.Examples[0].Ts.Offset.ShouldBe(TimeSpan.FromMinutes(330));
    }
}
