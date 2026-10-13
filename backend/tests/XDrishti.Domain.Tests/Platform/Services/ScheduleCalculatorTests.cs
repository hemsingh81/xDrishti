using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Domain.Tests.Platform.Services.ServiceTestData;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>
/// The pure answers about a schedule (ADR 0018 §3): occurrences on a date, the next one after an instant, the latest one up to an instant and whether one was missed — for every schedule type, on weekdays,
/// weekends, holidays and special sessions, across midnight India time. Mon 2026-10-12, Sat 10th, Sun 11th, Fri 9th.
/// </summary>
public sealed class ScheduleCalculatorTests
{
    private static readonly Schedule EndOfDay = Schedule.Daily(T(15, 50));

    private static readonly TradingCalendar Plain = new();

    // ---- daily on trading days with the session end rule (the end-of-day run, ADR 0017 §2) ----

    [Theory]
    [InlineData(15, 49, 12)]
    [InlineData(15, 50, 13)] // exactly at the time: that run is no longer "after"
    [InlineData(16, 0, 13)]
    public void A_daily_run_on_a_weekday_is_next_at_the_time_or_tomorrows_when_it_has_passed(int hour, int minute, int nextDay)
    {
        ScheduleCalculator.NextAfter(EndOfDay, At(10, 12, hour, minute), Plain, notBeforeSessionEnd: true).ShouldBe(At(10, nextDay, 15, 50));
    }

    [Theory]
    [InlineData(15, 49, false)]
    [InlineData(15, 50, true)]
    [InlineData(16, 0, true)]
    [InlineData(23, 59, true)]
    public void A_daily_run_is_missed_from_its_time_until_the_day_ends_when_no_run_started_since(int hour, int minute, bool missed)
    {
        var due = ScheduleCalculator.MissedOccurrence(EndOfDay, At(10, 12, hour, minute), null, Plain, notBeforeSessionEnd: true);

        due.HasValue.ShouldBe(missed);
        if (missed)
        {
            due.ShouldBe(At(10, 12, 15, 50));
        }
    }

    [Fact]
    public void A_run_that_started_at_or_after_the_time_means_nothing_was_missed()
    {
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(10, 12, 17, 0), At(10, 12, 15, 50), Plain, true).ShouldBeNull();
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(10, 12, 17, 0), At(10, 12, 15, 49, 59), Plain, true).ShouldBe(At(10, 12, 15, 50));
    }

    [Fact]
    public void After_midnight_India_time_yesterdays_time_is_no_longer_made_up_for_but_the_latest_reach_still_is()
    {
        var now = At(10, 13, 0, 5);

        ScheduleCalculator.MissedOccurrence(EndOfDay, now, null, Plain, true).ShouldBeNull();
        ScheduleCalculator.MissedOccurrence(EndOfDay, now, null, Plain, true, CatchUpReach.Latest).ShouldBe(At(10, 12, 15, 50));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void A_weekend_has_no_run_and_the_next_one_is_monday(int day)
    {
        ScheduleCalculator.OccurrencesOn(EndOfDay, Date(10, day), Plain, true).ShouldBeEmpty();
        ScheduleCalculator.NextAfter(EndOfDay, At(10, day, 10, 0), Plain, true).ShouldBe(At(10, 12, 15, 50));
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(10, day, 18, 0), null, Plain, true).ShouldBeNull();
    }

    [Fact]
    public void On_a_friday_after_the_run_the_next_run_is_monday()
    {
        ScheduleCalculator.NextAfter(EndOfDay, At(10, 9, 16, 0), Plain, true).ShouldBe(At(10, 12, 15, 50));
    }

    [Fact]
    public void A_holiday_has_no_run_and_the_next_one_skips_it()
    {
        var calendar = new TradingCalendar([Holiday(10, 13)]);

        ScheduleCalculator.NextAfter(EndOfDay, At(10, 12, 16, 0), calendar, true).ShouldBe(At(10, 14, 15, 50));
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(10, 13, 18, 0), null, calendar, true).ShouldBeNull();
    }

    [Fact]
    public void A_proposed_holiday_does_not_stop_the_run()
    {
        var proposed = TradingDay.Holiday(Date(10, 13), CalendarSource.Observed, CalendarStatus.Proposed, null, Stamp);

        ScheduleCalculator.NextAfter(EndOfDay, At(10, 12, 16, 0), new TradingCalendar([proposed]), true).ShouldBe(At(10, 13, 15, 50));
    }

    [Fact]
    public void A_special_session_on_a_weekend_gets_a_run_on_that_day()
    {
        var calendar = new TradingCalendar([Special(10, 10, 10 * 60, 11 * 60)]); // Saturday 10:00-11:00

        ScheduleCalculator.NextAfter(EndOfDay, At(10, 9, 16, 0), calendar, true).ShouldBe(At(10, 10, 15, 50));
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(10, 10, 16, 0), null, calendar, true).ShouldBe(At(10, 10, 15, 50));
    }

    [Fact]
    public void A_special_evening_session_moves_the_run_to_its_end()
    {
        var calendar = new TradingCalendar([Special(11, 8, 18 * 60 + 15, 19 * 60 + 15)]); // Muhurat, a Sunday

        ScheduleCalculator.OccurrencesOn(EndOfDay, Date(11, 8), calendar, true).ShouldBe([At(11, 8, 19, 15)]);
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(11, 8, 15, 55), null, calendar, true).ShouldBeNull();
        ScheduleCalculator.MissedOccurrence(EndOfDay, At(11, 8, 19, 15), null, calendar, true).ShouldBe(At(11, 8, 19, 15));
        ScheduleCalculator.NextAfter(EndOfDay, At(11, 6, 16, 0), calendar, true).ShouldBe(At(11, 8, 19, 15));
    }

    [Fact]
    public void A_time_before_the_close_waits_for_the_close_only_when_the_service_asks_for_it()
    {
        var early = Schedule.Daily(T(15, 0));

        ScheduleCalculator.OccurrencesOn(early, Date(10, 12), Plain, notBeforeSessionEnd: true).ShouldBe([At(10, 12, 15, 30)]);
        ScheduleCalculator.OccurrencesOn(early, Date(10, 12), Plain, notBeforeSessionEnd: false).ShouldBe([At(10, 12, 15, 0)]);
    }

    [Fact]
    public void A_calendar_with_no_trading_day_for_over_a_year_has_no_next_run()
    {
        var holidays = Enumerable.Range(0, 600).Select(i => Date(10, 12).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp));

        ScheduleCalculator.NextAfter(EndOfDay, At(10, 12, 10, 0), new TradingCalendar(holidays), true).ShouldBeNull();
    }

    // ---- daily on chosen days (not trading days): the instrument refresh, a backup ----

    [Fact]
    public void A_daily_run_on_every_day_fires_on_weekends_and_holidays_and_rolls_over_the_day()
    {
        var everyDay = Schedule.Daily(T(8, 45), tradingDaysOnly: false, Weekdays.EveryDay);
        var calendar = new TradingCalendar([Holiday(10, 13)]);

        ScheduleCalculator.NextAfter(everyDay, At(10, 10, 9, 0), calendar).ShouldBe(At(10, 11, 8, 45));
        ScheduleCalculator.NextAfter(everyDay, At(10, 12, 9, 0), calendar).ShouldBe(At(10, 13, 8, 45));
    }

    [Fact]
    public void Chosen_days_decide_when_trading_days_is_off()
    {
        var wednesdayAndSaturday = Schedule.Daily(T(23, 30), tradingDaysOnly: false, Weekdays.Wednesday | Weekdays.Saturday);

        ScheduleCalculator.NextAfter(wednesdayAndSaturday, At(10, 12, 12, 0), Plain).ShouldBe(At(10, 14, 23, 30));
        ScheduleCalculator.NextAfter(wednesdayAndSaturday, At(10, 14, 23, 30), Plain).ShouldBe(At(10, 17, 23, 30));
    }

    [Fact]
    public void The_India_day_decides_not_the_utc_day()
    {
        // 2026-10-11 20:00 UTC is 2026-10-12 01:30 IST: the 12th is the day, 08:45 is still ahead
        var daily = Schedule.Daily(T(8, 45), false, Weekdays.EveryDay);

        ScheduleCalculator.NextAfter(daily, new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.Zero), Plain).ShouldBe(At(10, 12, 8, 45));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(23, 59)]
    public void The_edges_of_the_day_work(int hour, int minute)
    {
        var daily = Schedule.Daily(T(hour, minute), false, Weekdays.EveryDay);

        var next = ScheduleCalculator.NextAfter(daily, At(10, 12, 12, 0), Plain);

        next.ShouldBe(At(10, 12, hour, minute).AddDays(hour == 0 ? 1 : 0));
    }

    [Fact]
    public void There_is_no_daylight_saving_shift_around_the_dates_other_zones_change()
    {
        var daily = Schedule.Daily(T(8, 45), false, Weekdays.EveryDay);

        foreach (var (month, day) in new[] { (3, 28), (3, 29), (10, 24), (10, 25), (11, 1), (3, 8) })
        {
            var next = ScheduleCalculator.NextAfter(daily, At(month, day, 9, 0), Plain);

            next.ShouldBe(At(month, day, 8, 45).AddDays(1));
            next!.Value.Offset.ShouldBe(Ist);
        }
    }

    [Theory]
    [InlineData(2026, 12, 31, 2027, 1, 1)] // year end
    [InlineData(2026, 2, 28, 2026, 3, 1)] // month end, not a leap year
    [InlineData(2028, 2, 28, 2028, 2, 29)] // leap day
    public void The_run_rolls_over_month_and_year_ends(int year, int month, int day, int nextYear, int nextMonth, int nextDay)
    {
        var daily = Schedule.Daily(T(8, 45), false, Weekdays.EveryDay);

        var next = ScheduleCalculator.NextAfter(daily, new DateTimeOffset(year, month, day, 9, 0, 0, Ist), Plain);

        next.ShouldBe(new DateTimeOffset(nextYear, nextMonth, nextDay, 8, 45, 0, Ist));
    }

    // ---- latest occurrence / reach ----

    [Fact]
    public void The_latest_occurrence_is_today_once_the_time_has_come_and_yesterday_before_it()
    {
        var daily = Schedule.Daily(T(8, 45), false, Weekdays.EveryDay);

        ScheduleCalculator.LatestAtOrBefore(daily, At(10, 11, 9, 0), Plain).ShouldBe(At(10, 11, 8, 45));
        ScheduleCalculator.LatestAtOrBefore(daily, At(10, 11, 8, 44), Plain).ShouldBe(At(10, 10, 8, 45));
        ScheduleCalculator.LatestAtOrBefore(daily, At(10, 11, 8, 45), Plain).ShouldBe(At(10, 11, 8, 45));
    }

    [Fact]
    public void The_latest_reach_makes_up_for_a_run_missed_days_ago_but_only_when_nothing_started_since()
    {
        var daily = Schedule.Daily(T(8, 45), false, Weekdays.EveryDay);
        var now = At(10, 12, 7, 30);

        ScheduleCalculator.MissedOccurrence(daily, now, At(10, 9, 8, 45), Plain, false, CatchUpReach.Latest).ShouldBe(At(10, 11, 8, 45));
        ScheduleCalculator.MissedOccurrence(daily, now, At(10, 11, 8, 45), Plain, false, CatchUpReach.Latest).ShouldBeNull();
        ScheduleCalculator.MissedOccurrence(daily, now, null, Plain, false, CatchUpReach.Latest).ShouldBe(At(10, 11, 8, 45));
        ScheduleCalculator.MissedOccurrence(daily, now, At(10, 9, 8, 45), Plain, false, CatchUpReach.SameDay).ShouldBeNull();
    }

    // ---- weekly ----

    [Fact]
    public void A_weekly_run_is_on_its_weekday_and_a_missed_one_is_made_up_for_within_24_hours()
    {
        var saturday = Schedule.Weekly(DayOfWeek.Saturday, T(10, 0));

        ScheduleCalculator.NextAfter(saturday, At(10, 12, 9, 0), Plain).ShouldBe(At(10, 17, 10, 0));
        ScheduleCalculator.NextAfter(saturday, At(10, 10, 9, 59), Plain).ShouldBe(At(10, 10, 10, 0));
        ScheduleCalculator.MissedOccurrence(saturday, At(10, 11, 9, 0), null, Plain).ShouldBe(At(10, 10, 10, 0)); // 23 h later, across midnight
        ScheduleCalculator.MissedOccurrence(saturday, At(10, 11, 10, 1), null, Plain).ShouldBeNull();
    }

    [Fact]
    public void A_weekly_run_with_trading_days_only_is_skipped_on_a_holiday()
    {
        var monday = Schedule.Weekly(DayOfWeek.Monday, T(9, 0), tradingDaysOnly: true);
        var calendar = new TradingCalendar([Holiday(10, 12)]);

        ScheduleCalculator.NextAfter(monday, At(10, 9, 12, 0), calendar).ShouldBe(At(10, 19, 9, 0));
        ScheduleCalculator.NextAfter(monday, At(10, 9, 12, 0), Plain).ShouldBe(At(10, 12, 9, 0));
    }

    // ---- after, continuous ----

    [Fact]
    public void A_service_that_follows_another_or_is_always_up_has_no_occurrences_of_its_own()
    {
        foreach (var schedule in new[] { Schedule.AfterService("eod"), Schedule.Continuous() })
        {
            ScheduleCalculator.OccurrencesOn(schedule, Date(10, 12), Plain).ShouldBeEmpty();
            ScheduleCalculator.NextAfter(schedule, At(10, 12, 9, 0), Plain).ShouldBeNull();
            ScheduleCalculator.LatestAtOrBefore(schedule, At(10, 12, 9, 0), Plain).ShouldBeNull();
            ScheduleCalculator.MissedOccurrence(schedule, At(10, 12, 9, 0), null, Plain).ShouldBeNull();
        }
    }

    [Fact]
    public void A_continuous_service_is_up_the_whole_day()
    {
        var band = ScheduleCalculator.BandsOn(Schedule.Continuous(), Date(10, 11), Plain).ShouldHaveSingleItem();

        band.ShouldBe((At(10, 11, 0, 0), At(10, 12, 0, 0)));
    }

    // ---- window ----

    [Fact]
    public void A_window_starts_at_its_start_on_trading_days_and_is_up_until_its_end()
    {
        var window = Schedule.Window(T(9, 0), T(15, 35));

        ScheduleCalculator.OccurrencesOn(window, Date(10, 12), Plain).ShouldBe([At(10, 12, 9, 0)]);
        ScheduleCalculator.BandsOn(window, Date(10, 12), Plain).ShouldHaveSingleItem().ShouldBe((At(10, 12, 9, 0), At(10, 12, 15, 35)));
        ScheduleCalculator.OccurrencesOn(window, Date(10, 10), Plain).ShouldBeEmpty();
        ScheduleCalculator.BandsOn(window, Date(10, 10), Plain).ShouldBeEmpty();
        ScheduleCalculator.NextAfter(window, At(10, 12, 9, 0), Plain).ShouldBe(At(10, 13, 9, 0));
    }

    // ---- interval ----

    [Fact]
    public void An_interval_ticks_from_the_start_of_its_window_and_nothing_after_its_end()
    {
        var every20 = Schedule.Interval(20, T(9, 15), T(10, 15));

        ScheduleCalculator.OccurrencesOn(every20, Date(10, 12), Plain)
            .ShouldBe([At(10, 12, 9, 15), At(10, 12, 9, 35), At(10, 12, 9, 55), At(10, 12, 10, 15)]);
        ScheduleCalculator.OccurrencesOn(Schedule.Interval(25, T(9, 15), T(10, 15)), Date(10, 12), Plain)
            .ShouldBe([At(10, 12, 9, 15), At(10, 12, 9, 40), At(10, 12, 10, 5)]);
    }

    [Fact]
    public void An_interval_every_minute_over_the_session_has_376_ticks_and_none_on_a_weekend()
    {
        var alerts = Schedule.Interval(1, T(9, 15), T(15, 30));

        ScheduleCalculator.OccurrencesOn(alerts, Date(10, 12), Plain).Count.ShouldBe(376);
        ScheduleCalculator.OccurrencesOn(alerts, Date(10, 11), Plain).ShouldBeEmpty();
    }

    [Fact]
    public void The_next_interval_tick_after_the_last_of_the_day_is_the_first_of_the_next_trading_day()
    {
        var alerts = Schedule.Interval(5, T(9, 15), T(15, 30));

        ScheduleCalculator.NextAfter(alerts, At(10, 12, 9, 17), Plain).ShouldBe(At(10, 12, 9, 20));
        ScheduleCalculator.NextAfter(alerts, At(10, 12, 15, 30), Plain).ShouldBe(At(10, 13, 9, 15));
        ScheduleCalculator.NextAfter(alerts, At(10, 9, 15, 31), Plain).ShouldBe(At(10, 12, 9, 15));
        ScheduleCalculator.LatestAtOrBefore(alerts, At(10, 12, 9, 17), Plain).ShouldBe(At(10, 12, 9, 15));
        ScheduleCalculator.LatestAtOrBefore(alerts, At(10, 12, 9, 0), Plain).ShouldBe(At(10, 9, 15, 30));
    }

    [Fact]
    public void An_interval_is_not_made_up_for()
    {
        ScheduleCalculator.MissedOccurrence(Schedule.Interval(5, T(9, 15), T(15, 30)), At(10, 12, 12, 0), null, Plain).ShouldBeNull();
    }

    // ---- preview ----

    [Fact]
    public void The_next_occurrences_are_listed_in_order_across_a_weekend_and_a_holiday()
    {
        var calendar = new TradingCalendar([Holiday(10, 14)]);

        var next = ScheduleCalculator.NextOccurrences(EndOfDay, At(10, 9, 16, 0), 3, calendar, true);

        next.ShouldBe([At(10, 12, 15, 50), At(10, 13, 15, 50), At(10, 15, 15, 50)]);
    }

    [Fact]
    public void Fewer_are_listed_when_the_schedule_has_no_more()
    {
        ScheduleCalculator.NextOccurrences(Schedule.Continuous(), At(10, 9, 16, 0), 3, Plain).ShouldBeEmpty();
    }
}
