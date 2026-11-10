using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets.Systems;

public sealed class ReviewWindowsTests
{
    private static readonly int[] Months = [3, 6, 9, 12];

    [Theory]
    [InlineData("2026-09-19", false)]
    [InlineData("2026-09-20", true)]
    [InlineData("2026-10-01", true)]
    [InlineData("2026-10-07", true)]
    [InlineData("2026-10-08", false)]
    [InlineData("2026-11-10", false)]
    [InlineData("2027-01-05", true)]
    [InlineData("2027-01-08", false)]
    public void The_window_runs_from_the_20th_of_a_review_month_to_the_7th_of_the_next(string date, bool inside)
    {
        ReviewWindows.Contains(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), Months, 20, 7).ShouldBe(inside);
    }
}
