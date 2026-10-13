using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>Thinning the daily points to weeks and months, and the resolution names.</summary>
public sealed class HistoryResolutionsTests
{
    /// <summary>Every weekday from Monday 5 Oct to Friday 6 Nov 2026.</summary>
    private static HistoryPoint[] Weekdays()
    {
        var points = new List<HistoryPoint>();
        for (var date = Oct(5); date <= new DateOnly(2026, 11, 6); date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                points.Add(new HistoryPoint(date, date.DayNumber, 0m, null, 100m, null));
            }
        }

        return [.. points];
    }

    [Fact]
    public void Day_keeps_every_point()
    {
        var points = Weekdays();

        HistoryResolutions.Thin(points, HistoryResolution.Day).ShouldBe(points);
    }

    [Fact]
    public void Week_keeps_the_last_trading_day_of_each_week()
    {
        var thinned = HistoryResolutions.Thin(Weekdays(), HistoryResolution.Week);

        thinned.Select(p => p.Date).ShouldBe([Oct(9), Oct(16), Oct(23), Oct(30), new DateOnly(2026, 11, 6)]);
    }

    [Fact]
    public void Week_takes_the_last_trading_day_when_the_friday_is_a_holiday()
    {
        var points = Weekdays().Where(p => p.Date != Oct(9)).ToArray();

        HistoryResolutions.Thin(points, HistoryResolution.Week).Select(p => p.Date).First().ShouldBe(Oct(8));
    }

    [Fact]
    public void Month_keeps_the_last_trading_day_of_each_month_and_the_newest_point()
    {
        var thinned = HistoryResolutions.Thin(Weekdays(), HistoryResolution.Month);

        thinned.Select(p => p.Date).ShouldBe([Oct(30), new DateOnly(2026, 11, 6)]); // November is not over: its last point so far stays
    }

    [Fact]
    public void A_week_that_runs_over_a_month_end_is_one_week_for_the_week_resolution()
    {
        var points = new[] { new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3) }
            .Select(d => new HistoryPoint(d, 1m, 1m, null, 100m, null)).ToArray();

        HistoryResolutions.Thin(points, HistoryResolution.Week).Select(p => p.Date).ShouldBe([new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 3)]);
        HistoryResolutions.Thin(points, HistoryResolution.Month).Select(p => p.Date).ShouldBe([new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 3)]);
    }

    [Fact]
    public void Fewer_than_two_points_are_returned_as_they_are()
    {
        HistoryResolutions.Thin([], HistoryResolution.Month).ShouldBeEmpty();
        HistoryResolutions.Thin(Weekdays()[..1], HistoryResolution.Week).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("day", HistoryResolution.Day)]
    [InlineData("WEEK", HistoryResolution.Week)]
    [InlineData(" month ", HistoryResolution.Month)]
    public void Names_are_read_ignoring_case_and_spaces(string text, HistoryResolution expected)
    {
        HistoryResolutions.TryParse(text, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(expected);
        HistoryResolutions.Name(expected).ShouldBe(text.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("year")]
    [InlineData(null)]
    public void Other_names_are_refused(string? text)
    {
        HistoryResolutions.TryParse(text, out _).ShouldBeFalse();
    }
}
