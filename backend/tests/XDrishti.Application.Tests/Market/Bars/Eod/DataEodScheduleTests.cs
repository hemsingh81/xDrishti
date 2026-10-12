using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars.Eod;

/// <summary>When the end-of-day run happens: trading days only, at the configured India time, never before the session has closed.</summary>
public sealed class DataEodScheduleTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly TimeOnly Time = new(15, 50);

    private static readonly DateTimeOffset Stamp = new(2026, 10, 1, 0, 0, 0, Ist);

    private static DateTimeOffset At(int month, int day, int hour, int minute, int second = 0) => new(2026, month, day, hour, minute, second, Ist);

    private static TradingDay Holiday(int month, int day) => TradingDay.Holiday(new DateOnly(2026, month, day), CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp);

    private static TradingDay Special(int month, int day, int fromMinute, int toMinute) =>
        TradingDay.Special(new DateOnly(2026, month, day), fromMinute, toMinute, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp);

    [Fact]
    public void At_14_59_the_run_of_the_day_is_ahead_and_not_yet_due()
    {
        var now = At(10, 12, 14, 59);

        DataEodSchedule.NextRun(now, Time, new TradingCalendar()).ShouldBe(At(10, 12, 15, 50));
        DataEodSchedule.IsDue(now, Time, new TradingCalendar()).ShouldBeFalse();
    }

    [Fact]
    public void At_exactly_15_50_the_run_is_due_and_the_next_one_is_tomorrows()
    {
        var now = At(10, 12, 15, 50);

        DataEodSchedule.IsDue(now, Time, new TradingCalendar()).ShouldBeTrue();
        DataEodSchedule.NextRun(now, Time, new TradingCalendar()).ShouldBe(At(10, 13, 15, 50));
    }

    [Fact]
    public void One_second_before_15_50_is_not_due_and_after_it_stays_due_for_the_rest_of_the_day()
    {
        DataEodSchedule.IsDue(At(10, 12, 15, 49, 59), Time, new TradingCalendar()).ShouldBeFalse();
        DataEodSchedule.IsDue(At(10, 12, 23, 59), Time, new TradingCalendar()).ShouldBeTrue();
        DataEodSchedule.IsDue(At(10, 13, 0, 0), Time, new TradingCalendar()).ShouldBeFalse(); // Tuesday has begun: Monday's run is no longer due, Tuesday's is not yet
    }

    [Fact]
    public void After_the_run_time_the_next_run_is_the_next_trading_day()
    {
        DataEodSchedule.NextRun(At(10, 12, 16, 30), Time, new TradingCalendar()).ShouldBe(At(10, 13, 15, 50));
    }

    [Fact]
    public void On_a_friday_after_the_run_the_next_run_is_monday()
    {
        DataEodSchedule.NextRun(At(10, 9, 16, 0), Time, new TradingCalendar()).ShouldBe(At(10, 12, 15, 50));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void A_weekend_has_no_run_and_is_never_due(int day)
    {
        var calendar = new TradingCalendar();

        DataEodSchedule.NextRun(At(10, day, 10, 0), Time, calendar).ShouldBe(At(10, 12, 15, 50));
        DataEodSchedule.IsDue(At(10, day, 18, 0), Time, calendar).ShouldBeFalse();
        DataEodSchedule.RunTime(calendar, new DateOnly(2026, 10, day), Time).ShouldBeNull();
    }

    [Fact]
    public void A_holiday_has_no_run_and_the_next_one_skips_it()
    {
        var calendar = new TradingCalendar([Holiday(10, 13)]);

        DataEodSchedule.NextRun(At(10, 12, 16, 0), Time, calendar).ShouldBe(At(10, 14, 15, 50));
        DataEodSchedule.IsDue(At(10, 13, 18, 0), Time, calendar).ShouldBeFalse();
    }

    [Fact]
    public void A_proposed_holiday_does_not_stop_the_run()
    {
        var proposed = TradingDay.Holiday(new DateOnly(2026, 10, 13), CalendarSource.Observed, CalendarStatus.Proposed, null, Stamp);

        DataEodSchedule.NextRun(At(10, 12, 16, 0), Time, new TradingCalendar([proposed])).ShouldBe(At(10, 13, 15, 50));
    }

    [Fact]
    public void A_special_session_on_a_weekend_gets_a_run_on_that_day()
    {
        var calendar = new TradingCalendar([Special(10, 10, 10 * 60, 11 * 60)]); // Saturday 10:00-11:00

        DataEodSchedule.NextRun(At(10, 9, 16, 0), Time, calendar).ShouldBe(At(10, 10, 15, 50));
        DataEodSchedule.IsDue(At(10, 10, 16, 0), Time, calendar).ShouldBeTrue();
    }

    [Fact]
    public void A_special_evening_session_moves_the_run_to_its_end()
    {
        var calendar = new TradingCalendar([Special(11, 8, 18 * 60 + 15, 19 * 60 + 15)]); // Muhurat, a Sunday

        DataEodSchedule.RunTime(calendar, new DateOnly(2026, 11, 8), Time).ShouldBe(At(11, 8, 19, 15));
        DataEodSchedule.IsDue(At(11, 8, 15, 55), Time, calendar).ShouldBeFalse();
        DataEodSchedule.IsDue(At(11, 8, 19, 15), Time, calendar).ShouldBeTrue();
        DataEodSchedule.NextRun(At(11, 6, 16, 0), Time, calendar).ShouldBe(At(11, 8, 19, 15));
    }

    [Fact]
    public void A_configured_time_before_the_close_still_waits_for_the_close()
    {
        DataEodSchedule.RunTime(new TradingCalendar(), new DateOnly(2026, 10, 12), new TimeOnly(15, 0)).ShouldBe(At(10, 12, 15, 30));
        DataEodSchedule.IsDue(At(10, 12, 15, 10), new TimeOnly(15, 0), new TradingCalendar()).ShouldBeFalse();
    }

    [Fact]
    public void A_calendar_with_no_trading_day_for_over_a_year_has_no_next_run()
    {
        var holidays = Enumerable.Range(0, 600).Select(i => new DateOnly(2026, 10, 12).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, Stamp));

        DataEodSchedule.NextRun(At(10, 12, 10, 0), Time, new TradingCalendar(holidays)).ShouldBeNull();
    }
}
