using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Baskets.Derived;

public sealed class DerivedScheduleTests
{
    private static TradingCalendar WithHolidays(params DateOnly[] holidays) =>
        new(holidays.Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)));

    [Fact]
    public void Next_session_after_a_friday_skips_the_weekend()
    {
        DerivedSchedule.NextSession(WithHolidays(), new DateOnly(2026, 10, 23)).ShouldBe(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void Next_session_skips_a_holiday_as_well_as_the_weekend()
    {
        var calendar = WithHolidays(new DateOnly(2026, 10, 26));

        DerivedSchedule.NextSession(calendar, new DateOnly(2026, 10, 23)).ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void A_daily_basket_is_due_on_every_session_and_never_on_a_closed_day()
    {
        var calendar = WithHolidays();

        DerivedSchedule.IsDue(DerivedCadence.Daily, calendar, new DateOnly(2026, 10, 22)).ShouldBeTrue();
        DerivedSchedule.IsDue(DerivedCadence.Daily, calendar, new DateOnly(2026, 10, 24)).ShouldBeFalse();
    }

    [Fact]
    public void A_weekly_basket_is_due_on_the_friday_and_not_on_the_thursday()
    {
        var calendar = WithHolidays();

        DerivedSchedule.IsDue(DerivedCadence.Weekly, calendar, new DateOnly(2026, 10, 23)).ShouldBeTrue();
        DerivedSchedule.IsDue(DerivedCadence.Weekly, calendar, new DateOnly(2026, 10, 22)).ShouldBeFalse();
    }

    [Fact]
    public void A_weekly_basket_moves_to_the_previous_session_when_the_friday_is_a_holiday()
    {
        var calendar = WithHolidays(new DateOnly(2026, 10, 23));

        DerivedSchedule.IsDue(DerivedCadence.Weekly, calendar, new DateOnly(2026, 10, 22)).ShouldBeTrue();
        DerivedSchedule.IsDue(DerivedCadence.Weekly, calendar, new DateOnly(2026, 10, 23)).ShouldBeFalse();
        DerivedSchedule.NextSession(calendar, new DateOnly(2026, 10, 22)).ShouldBe(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void A_monthly_basket_is_due_on_the_last_session_of_the_month()
    {
        var calendar = WithHolidays();

        DerivedSchedule.IsDue(DerivedCadence.Monthly, calendar, new DateOnly(2026, 10, 30)).ShouldBeTrue();
        DerivedSchedule.IsDue(DerivedCadence.Monthly, calendar, new DateOnly(2026, 10, 29)).ShouldBeFalse();
    }

    [Fact]
    public void A_frozen_intraday_basket_is_never_due_here()
    {
        DerivedSchedule.IsDue(DerivedCadence.FrozenIntraday, WithHolidays(), new DateOnly(2026, 10, 23)).ShouldBeFalse();
    }

    [Fact]
    public void The_next_due_date_of_a_weekly_basket_is_the_next_last_session_of_a_week()
    {
        DerivedSchedule.NextDue(DerivedCadence.Weekly, WithHolidays(), new DateOnly(2026, 10, 23)).ShouldBe(new DateOnly(2026, 10, 30));
    }

    [Theory]
    [InlineData(13, "flags_unavailable")]
    [InlineData(4, "intraday_unavailable")]
    [InlineData(12, "intraday_unavailable")]
    [InlineData(3, null)]
    public void Only_baskets_this_build_can_form_are_runnable(int id, string? reason)
    {
        DerivedSchedule.WhyNotRunnable(DerivedCatalogue.Get(id)).ShouldBe(reason);
    }
}
