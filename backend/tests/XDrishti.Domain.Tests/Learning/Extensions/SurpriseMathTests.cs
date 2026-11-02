using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P15-05 criterion 2 (hand values to 1e-12): the surprise, the growth and the margin arithmetic, the EPS adjustment and the quarter arithmetic.</summary>
public sealed class SurpriseMathTests
{
    private const double Tolerance = 1e-12;

    // eight differences with a sample standard deviation of exactly 1: four of +x and four of -x with 8 x^2 = 7
    private static readonly double X = Math.Sqrt(7.0 / 8.0);

    /// <summary>EPS newest first such that eps[i] - eps[i+4] = d[i] for i = 0..8, with the newest difference <paramref name="newest"/> and the eight before it +-X.</summary>
    private static double[] EpsWithDifferences(double newest, int count = 13)
    {
        var d = new double[9];
        d[0] = newest;
        for (var i = 1; i <= 8; i++)
        {
            d[i] = i % 2 == 0 ? X : -X;
        }

        var eps = new double[count];
        for (var i = count - 1; i >= 0; i--)
        {
            eps[i] = i >= 9 ? 10.0 + i : d[i] + eps[i + 4];
        }

        return eps;
    }

    [Fact]
    public void The_hand_example_of_the_plan_gives_sue_yoy_of_two()
    {
        var eps = EpsWithDifferences(2.0);

        SurpriseMath.SueYoy(eps).ShouldBe(2.0, Tolerance);
        (eps[0] - eps[4]).ShouldBe(2.0, Tolerance);
    }

    [Fact]
    public void Sue_yoy_needs_thirteen_quarters_and_is_nan_when_the_deviation_is_zero()
    {
        SurpriseMath.SueYoy(EpsWithDifferences(2.0)[..12]).ShouldBeNaN();
        SurpriseMath.SueYoy([.. Enumerable.Repeat(1.0, 13)]).ShouldBeNaN("every difference is 0: the deviation is 0");
    }

    [Fact]
    public void A_deviation_that_is_only_rounding_noise_is_undefined_not_a_huge_surprise()
    {
        // every year-on-year difference is 4 up to 1e-13: the deviation is noise, so the surprise is undefined (relative tolerance 1e-9 of the differences)
        double[] eps = [.. Enumerable.Range(0, 13).Select(i => 100.0 - i + (i % 3 == 0 ? 1e-13 : 0))];
        var diffs = Enumerable.Range(0, 9).Select(i => eps[i] - eps[i + 4]).ToArray();

        SurpriseMath.SampleSd(diffs[1..]).ShouldBeGreaterThan(0.0);
        SurpriseMath.SueYoy(eps).ShouldBeNaN();
        SurpriseMath.SueYoy(EpsWithDifferences(2.0)).ShouldBe(2.0, Tolerance, "a real deviation of 1 is far above the tolerance");
    }

    [Fact]
    public void Sue_ttm_uses_the_four_quarter_sums_and_needs_sixteen_quarters()
    {
        // ttm[i] = sum of eps[i..i+3]; with a constant series of 2 the TTM is 8 everywhere, so only a step in the newest four quarters moves it
        var eps = Enumerable.Range(0, 16).Select(i => 2.0 + (i % 3 == 0 ? 0.25 : 0) + (i < 4 ? 1.0 : 0)).ToArray();
        var ttm = Enumerable.Range(0, 13).Select(i => eps[i] + eps[i + 1] + eps[i + 2] + eps[i + 3]).ToArray();
        var diffs = Enumerable.Range(0, 9).Select(i => ttm[i] - ttm[i + 4]).ToArray();
        var mean = diffs[1..].Average();
        var sd = Math.Sqrt(diffs[1..].Sum(d => (d - mean) * (d - mean)) / 7);

        SurpriseMath.SueTtm(eps).ShouldBe(diffs[0] / sd, Tolerance);
        SurpriseMath.SueTtm(eps[..15]).ShouldBeNaN();
        SurpriseMath.Ttm(eps, 0).ShouldBe(ttm[0], Tolerance);
        SurpriseMath.Ttm(eps[..3], 0).ShouldBeNaN();
    }

    [Fact]
    public void Growth_and_margin_change_are_hand_values_and_nan_on_a_base_of_zero_or_less()
    {
        SurpriseMath.Growth(120, 100).ShouldBe(0.2, Tolerance);
        SurpriseMath.Growth(80, 100).ShouldBe(-0.2, Tolerance);
        SurpriseMath.Growth(5, 0).ShouldBeNaN();
        SurpriseMath.Growth(5, -3).ShouldBeNaN();

        SurpriseMath.MarginChange(10, 100, 8, 100).ShouldBe(2.0, Tolerance);
        SurpriseMath.MarginChange(10, 200, 8, 100).ShouldBe(-3.0, Tolerance);
        SurpriseMath.MarginChange(10, 0, 8, 100).ShouldBeNaN();
        SurpriseMath.MarginChange(10, 100, 8, 0).ShouldBeNaN();
    }

    [Fact]
    public void The_percentile_counts_values_below_plus_half_the_ties_and_needs_the_minimum()
    {
        double[] history = [1, 2, 3, 4, 5];

        SurpriseMath.Percentile(3.5, history, 5).ShouldBe(60.0, Tolerance);
        SurpriseMath.Percentile(3, history, 5).ShouldBe(50.0, Tolerance);
        SurpriseMath.Percentile(0, history, 5).ShouldBe(0.0, Tolerance);
        SurpriseMath.Percentile(9, history, 5).ShouldBe(100.0, Tolerance);
        SurpriseMath.Percentile(3, history, 6).ShouldBeNaN();
        SurpriseMath.Percentile(3, [], 0).ShouldBeNaN();
    }

    [Fact]
    public void A_split_after_the_year_ago_quarter_puts_its_eps_on_the_new_share_basis()
    {
        // a 1:2 split with the ex-date after the year-ago quarter: the price factor 0.5 halves the year-ago EPS of 20 to 10
        var split = new AppliedAction(new DateOnly(2025, 12, 1), CorporateActionKind.Split, 1, 2, 0.5m);
        var later = new AppliedAction(new DateOnly(2026, 9, 1), CorporateActionKind.Bonus, 1, 1, 0.5m);

        EpsAdjustment.Factor([split], new DateOnly(2025, 6, 30), new DateOnly(2026, 7, 28)).ShouldBe(0.5, Tolerance);
        EpsAdjustment.Factor([split], new DateOnly(2025, 12, 31), new DateOnly(2026, 7, 28)).ShouldBe(1.0, Tolerance, "the quarter ended after the ex-date");
        EpsAdjustment.Factor([split], new DateOnly(2025, 6, 30), new DateOnly(2025, 11, 30)).ShouldBe(1.0, Tolerance, "the split was not yet known");
        EpsAdjustment.Factor([split], new DateOnly(2025, 6, 30), new DateOnly(2025, 12, 1)).ShouldBe(0.5, Tolerance, "the ex-date itself counts");
        EpsAdjustment.Factor([split, later], new DateOnly(2025, 6, 30), new DateOnly(2026, 7, 28)).ShouldBe(0.5, Tolerance, "an action after the date is never applied");
        EpsAdjustment.Factor([split, later], new DateOnly(2025, 6, 30), new DateOnly(2026, 9, 1)).ShouldBe(0.25, Tolerance);
        EpsAdjustment.SinceOf(new DateTimeOffset(2025, 7, 30, 6, 0, 0, TimeSpan.Zero), new DateOnly(2025, 6, 30)).ShouldBe(new DateOnly(2025, 7, 30));
        EpsAdjustment.SinceOf(null, new DateOnly(2025, 6, 30)).ShouldBe(new DateOnly(2025, 6, 30));
    }

    [Theory]
    [InlineData(2026, 6, 30, 0, 2026, 6, 30)]
    [InlineData(2026, 6, 30, 1, 2026, 3, 31)]
    [InlineData(2026, 6, 30, 4, 2025, 6, 30)]
    [InlineData(2026, 3, 31, 1, 2025, 12, 31)]
    [InlineData(2026, 3, 31, 5, 2024, 12, 31)]
    public void Quarter_ends_step_back_to_the_last_day_of_the_month_three_months_earlier(int y, int m, int d, int back, int ey, int em, int ed)
    {
        QuarterEnds.Back(new DateOnly(y, m, d), back).ShouldBe(new DateOnly(ey, em, ed));
    }
}
