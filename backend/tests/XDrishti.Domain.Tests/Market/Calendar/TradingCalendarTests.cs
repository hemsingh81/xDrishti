using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Market.Calendar;

/// <summary>What a session is: weekdays, minus the confirmed holidays, plus the confirmed special sessions (ADR 0017 §1).</summary>
public sealed class TradingCalendarTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset India(int year, int month, int day, int hour, int minute, int second = 0) => new(year, month, day, hour, minute, second, TimeSpan.FromMinutes(330));

    private static TradingDay Holiday(int y, int m, int d, CalendarStatus status = CalendarStatus.Confirmed) =>
        TradingDay.Holiday(new DateOnly(y, m, d), CalendarSource.Manual, status, null, Stamp);

    private static TradingDay Special(int y, int m, int d, int fromMinute, int toMinute, CalendarStatus status = CalendarStatus.Confirmed) =>
        TradingDay.Special(new DateOnly(y, m, d), fromMinute, toMinute, CalendarSource.Manual, status, null, Stamp);

    private static readonly TradingDay Muhurat = Special(2024, 11, 1, 18 * 60 + 15, 19 * 60 + 15);

    private static readonly TradingDay SaturdaySession = Special(2024, 3, 2, 9 * 60 + 15, 10 * 60 + 15);

    [Fact]
    public void Without_any_entry_every_weekday_has_the_regular_session_and_the_weekend_none()
    {
        var calendar = new TradingCalendar();

        calendar.IsTradingDay(new DateOnly(2026, 10, 9)).ShouldBeTrue(); // Friday
        calendar.IsTradingDay(new DateOnly(2026, 10, 10)).ShouldBeFalse(); // Saturday
        calendar.IsTradingDay(new DateOnly(2026, 10, 11)).ShouldBeFalse(); // Sunday
        calendar.SessionOf(new DateOnly(2026, 10, 9)).ShouldBe(new TradingSession(new DateOnly(2026, 10, 9), 9 * 60 + 15, 15 * 60 + 30));
        calendar.SessionOf(new DateOnly(2026, 10, 10)).ShouldBeNull();
        calendar.ExpectedMinutes(new DateOnly(2026, 10, 9)).ShouldBe(375);
        calendar.ExpectedMinutes(new DateOnly(2026, 10, 10)).ShouldBe(0);
    }

    [Fact]
    public void A_confirmed_holiday_is_not_a_trading_day()
    {
        var calendar = new TradingCalendar([Holiday(2026, 10, 2)]);

        calendar.IsTradingDay(new DateOnly(2026, 10, 2)).ShouldBeFalse();
        calendar.SessionOf(new DateOnly(2026, 10, 2)).ShouldBeNull();
        calendar.ExpectedMinutes(new DateOnly(2026, 10, 2)).ShouldBe(0);
        calendar.IsTradingDay(new DateOnly(2026, 10, 1)).ShouldBeTrue();
    }

    [Fact]
    public void A_proposed_or_dismissed_day_changes_nothing()
    {
        var calendar = new TradingCalendar([Holiday(2026, 10, 2, CalendarStatus.Proposed), Holiday(2026, 10, 1, CalendarStatus.Dismissed), Special(2026, 10, 10, 600, 660, CalendarStatus.Proposed)]);

        calendar.IsTradingDay(new DateOnly(2026, 10, 2)).ShouldBeTrue();
        calendar.IsTradingDay(new DateOnly(2026, 10, 1)).ShouldBeTrue();
        calendar.IsTradingDay(new DateOnly(2026, 10, 10)).ShouldBeFalse();
        calendar.NextHoliday(new DateOnly(2026, 9, 1)).ShouldBeNull();
    }

    [Fact]
    public void Muhurat_on_a_weekday_holiday_is_a_session_with_its_own_window()
    {
        var calendar = new TradingCalendar([Muhurat]);

        calendar.IsTradingDay(new DateOnly(2024, 11, 1)).ShouldBeTrue();
        calendar.SessionOf(new DateOnly(2024, 11, 1)).ShouldBe(new TradingSession(new DateOnly(2024, 11, 1), 1095, 1155));
        calendar.ExpectedMinutes(new DateOnly(2024, 11, 1)).ShouldBe(60);
        calendar.SessionOf(new DateOnly(2024, 11, 1))!.Value.IsRegular.ShouldBeFalse();
    }

    [Fact]
    public void A_saturday_special_session_is_a_trading_day_with_its_length()
    {
        var calendar = new TradingCalendar([SaturdaySession]);

        calendar.IsTradingDay(new DateOnly(2024, 3, 2)).ShouldBeTrue();
        calendar.ExpectedMinutes(new DateOnly(2024, 3, 2)).ShouldBe(60);
        calendar.IsTradingDay(new DateOnly(2024, 3, 3)).ShouldBeFalse(); // the Sunday after
    }

    [Theory]
    [InlineData(2026, 10, 12, 12, 0, 2026, 10, 9)]   // Monday during the session: Friday
    [InlineData(2026, 10, 12, 15, 29, 2026, 10, 9)]  // one minute before the close
    [InlineData(2026, 10, 12, 15, 30, 2026, 10, 12)] // at the close: today
    [InlineData(2026, 10, 12, 23, 0, 2026, 10, 12)]
    [InlineData(2026, 10, 13, 0, 0, 2026, 10, 12)]   // Tuesday just after midnight: Monday
    [InlineData(2026, 10, 10, 12, 0, 2026, 10, 9)]   // Saturday: Friday
    [InlineData(2026, 10, 11, 20, 0, 2026, 10, 9)]   // Sunday: Friday
    [InlineData(2026, 10, 9, 16, 0, 2026, 10, 9)]    // Friday after the close
    public void The_last_closed_session_without_exceptions_is_the_last_weekday_that_has_closed(int y, int m, int d, int hour, int minute, int ey, int em, int ed) =>
        new TradingCalendar().LastClosedSession(India(y, m, d, hour, minute)).ShouldBe(new DateOnly(ey, em, ed));

    [Theory]
    [InlineData(2026, 10, 12, 15, 29, 59, 2026, 10, 9)]
    [InlineData(2026, 10, 12, 15, 30, 0, 2026, 10, 12)]
    public void The_last_closed_session_changes_at_exactly_the_close(int y, int m, int d, int hour, int minute, int second, int ey, int em, int ed) =>
        new TradingCalendar().LastClosedSession(India(y, m, d, hour, minute, second)).ShouldBe(new DateOnly(ey, em, ed));

    [Fact]
    public void The_date_is_the_india_date_not_the_utc_date()
    {
        // 2026-10-12 21:00 UTC is Tuesday 02:30 in India.
        new TradingCalendar().LastClosedSession(new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public void A_holiday_is_skipped_when_looking_for_the_last_closed_session()
    {
        var calendar = new TradingCalendar([Holiday(2026, 10, 9), Holiday(2026, 10, 12)]); // Friday and Monday: a long weekend

        calendar.LastClosedSession(India(2026, 10, 12, 16, 0)).ShouldBe(new DateOnly(2026, 10, 8)); // the Monday itself is a holiday
        calendar.LastClosedSession(India(2026, 10, 13, 10, 0)).ShouldBe(new DateOnly(2026, 10, 8)); // Tuesday morning: Thursday still
        calendar.LastClosedSession(India(2026, 10, 13, 16, 0)).ShouldBe(new DateOnly(2026, 10, 13));
    }

    [Fact]
    public void On_a_muhurat_evening_the_session_closes_at_its_own_end_not_at_half_past_three()
    {
        var calendar = new TradingCalendar([Muhurat]);

        calendar.LastClosedSession(India(2024, 11, 1, 16, 0)).ShouldBe(new DateOnly(2024, 10, 31)); // the evening session has not even started
        calendar.LastClosedSession(India(2024, 11, 1, 19, 14)).ShouldBe(new DateOnly(2024, 10, 31));
        calendar.LastClosedSession(India(2024, 11, 1, 19, 15)).ShouldBe(new DateOnly(2024, 11, 1));
        calendar.LastClosedSession(India(2024, 11, 2, 9, 0)).ShouldBe(new DateOnly(2024, 11, 1)); // Saturday morning: the Muhurat day (a normal Saturday has none)
    }

    [Fact]
    public void A_saturday_special_session_is_the_last_closed_session_once_it_has_ended()
    {
        var calendar = new TradingCalendar([SaturdaySession]);

        calendar.LastClosedSession(India(2024, 3, 2, 10, 14)).ShouldBe(new DateOnly(2024, 3, 1));
        calendar.LastClosedSession(India(2024, 3, 2, 10, 15)).ShouldBe(new DateOnly(2024, 3, 2));
        calendar.LastClosedSession(India(2024, 3, 4, 9, 0)).ShouldBe(new DateOnly(2024, 3, 2)); // Monday before the open: the Saturday session, not Friday
    }

    [Fact]
    public void A_calendar_that_is_all_holidays_still_answers()
    {
        var days = Enumerable.Range(1, 4000).Select(i => new DateOnly(2016, 1, 1).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp));
        var calendar = new TradingCalendar(days);

        calendar.LastClosedSession(India(2026, 10, 12, 16, 0)).ShouldBeLessThan(new DateOnly(2026, 10, 12));
    }

    [Theory]
    [InlineData(2026, 10, 9, 2026, 10, 9, 0)]
    [InlineData(2026, 10, 12, 2026, 10, 9, 0)]  // newer than expected: not behind
    [InlineData(2026, 10, 8, 2026, 10, 9, 1)]
    [InlineData(2026, 10, 7, 2026, 10, 9, 2)]
    [InlineData(2026, 10, 9, 2026, 10, 12, 1)]  // Friday's data on Monday evening: only Monday is missing, the weekend does not count
    [InlineData(2026, 10, 2, 2026, 10, 12, 6)]
    [InlineData(2026, 10, 10, 2026, 10, 12, 1)] // a Saturday bar: Monday is missing
    public void Trading_days_between_count_the_weekdays_after_the_first_date_up_to_the_second(int ly, int lm, int ld, int ey, int em, int ed, int behind) =>
        new TradingCalendar().TradingDaysBetween(new DateOnly(ly, lm, ld), new DateOnly(ey, em, ed)).ShouldBe(behind);

    [Fact]
    public void A_long_weekend_does_not_count_as_behind()
    {
        var calendar = new TradingCalendar([Holiday(2026, 10, 12)]); // Monday is a holiday

        // Friday's data, expected session Friday (the Monday holiday never closed a session): current.
        calendar.TradingDaysBetween(new DateOnly(2026, 10, 9), calendar.LastClosedSession(India(2026, 10, 12, 18, 0))).ShouldBe(0);
        // Tuesday after the close: only the Tuesday is missing.
        calendar.TradingDaysBetween(new DateOnly(2026, 10, 9), calendar.LastClosedSession(India(2026, 10, 13, 18, 0))).ShouldBe(1);
        // Wednesday morning, Thursday's bar missing, Tuesday's missing: Tuesday only (Wednesday has not closed).
        calendar.TradingDaysBetween(new DateOnly(2026, 10, 9), calendar.LastClosedSession(India(2026, 10, 14, 9, 0))).ShouldBe(1);
    }

    [Fact]
    public void Special_sessions_count_as_trading_days_between_dates()
    {
        var calendar = new TradingCalendar([SaturdaySession, Holiday(2024, 3, 1)]);

        calendar.TradingDaysBetween(new DateOnly(2024, 2, 29), new DateOnly(2024, 3, 4)).ShouldBe(2); // Saturday 2nd and Monday 4th (Friday is a holiday)
    }

    [Fact]
    public void The_next_holiday_is_the_first_confirmed_one_on_or_after_the_date()
    {
        var calendar = new TradingCalendar([Holiday(2026, 11, 8), Holiday(2026, 10, 2), Holiday(2026, 12, 25), Muhurat]);

        calendar.NextHoliday(new DateOnly(2026, 10, 2))!.Id.ShouldBe(new DateOnly(2026, 10, 2));
        calendar.NextHoliday(new DateOnly(2026, 10, 3))!.Id.ShouldBe(new DateOnly(2026, 11, 8));
        calendar.NextHoliday(new DateOnly(2026, 12, 26)).ShouldBeNull();
    }
}
