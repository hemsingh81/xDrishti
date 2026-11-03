using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Market.Eod;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>CF-04 criterion 15: daily versus minute is a finding, never a gate; the close is not compared; the high is an envelope.</summary>
public sealed class DailyMinuteComparerTests
{
    private static readonly DateOnly Day = new(2024, 7, 5);

    private static DailyMinuteRow Row(long daily, long minute, decimal dailyHigh = 101m, decimal minuteHigh = 101m) => new("ABC", Day, daily, minute, dailyHigh, minuteHigh, 0.05m);

    [Fact]
    public void Half_a_percent_apart_is_inside_the_two_percent_tolerance_and_silent() =>
        DailyMinuteComparer.Compare([Row(10_000_000, 9_950_000)], 2).ShouldBeEmpty();

    [Fact]
    public void Twelve_percent_apart_raises_daily_minute_mismatch()
    {
        var findings = DailyMinuteComparer.Compare([Row(10_000_000, 8_800_000)], 2);

        findings.Single().ShouldSatisfyAllConditions(f => f.Code.ShouldBe("daily_minute_mismatch"), f => f.Field.ShouldBe("volume"));
    }

    [Fact]
    public void The_daily_high_may_be_one_tick_below_the_highest_minute_high_but_not_more()
    {
        DailyMinuteComparer.Compare([Row(1000, 1000, dailyHigh: 100.00m, minuteHigh: 100.05m)], 2).ShouldBeEmpty();
        DailyMinuteComparer.Compare([Row(1000, 1000, dailyHigh: 100.00m, minuteHigh: 100.10m)], 2).Single().Field.ShouldBe("high");
    }

    [Fact]
    public void A_day_with_no_daily_quantity_is_not_divided_by_zero() =>
        DailyMinuteComparer.Compare([Row(0, 500)], 2).ShouldBeEmpty();
}
