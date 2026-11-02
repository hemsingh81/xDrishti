using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Seasons are calendar quarters; the last four with data are the hold-out (P15-10 common rules).</summary>
public sealed class SeasonFoldsTests
{
    [Theory]
    [InlineData(2024, 1, 1, "2024Q1")]
    [InlineData(2024, 3, 31, "2024Q1")]
    [InlineData(2024, 4, 1, "2024Q2")]
    [InlineData(2024, 12, 31, "2024Q4")]
    public void A_season_is_the_calendar_quarter(int y, int m, int d, string expected)
    {
        Season.Of(new DateOnly(y, m, d)).ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_hold_out_starts_at_the_fourth_last_season_with_data_and_not_at_all_with_four_or_fewer()
    {
        var dates = Enumerable.Range(0, 7).Select(i => new DateOnly(2023, 2, 1).AddMonths(3 * i)).ToList();
        var seasons = SeasonFolds.Distinct(dates);

        seasons.Count.ShouldBe(7);
        SeasonFolds.HoldOutStart(seasons).ShouldBe(new DateOnly(2023, 10, 1));
        SeasonFolds.HoldOutStart([.. seasons.Take(4)]).ShouldBeNull();
    }

    [Fact]
    public void The_split_gives_the_choice_the_test_and_the_last_four_and_a_short_history_a_short_test()
    {
        var seasons = Enumerable.Range(0, 22).Select(i => new Season(2018 + (i / 4), (i % 4) + 1)).ToList();

        var (choice, test, hold) = SeasonFolds.Split(seasons, 8);
        var (c2, t2, h2) = SeasonFolds.Split([.. seasons.Take(10)], 8);

        (choice.Count, test.Count, hold.Count).ShouldBe((8, 10, 4));
        (c2.Count, t2.Count, h2.Count).ShouldBe((6, 0, 4));
    }
}
