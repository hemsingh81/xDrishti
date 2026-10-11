using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>When the daily holdings refresh is due (ADR 0013). All instants are India time (UTC+05:30). 2026-10-12 is a Monday.</summary>
public sealed class PortfolioRefreshScheduleTests
{
    private static readonly SyncActivity Never = new((DateTimeOffset?)null);

    private static PortfolioRefresh Daily(int hour = 16, int minute = 0) => new(PortfolioRefreshMode.Daily, new TimeOnly(hour, minute));

    private static readonly PortfolioRefresh Off = PortfolioRefresh.Default;

    private static DateTimeOffset Ist(int month, int day, int hour, int minute, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.FromMinutes(330));

    [Fact]
    public void A_refresh_that_is_off_is_never_due_and_has_no_next_run()
    {
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 10, 0), Off, Never).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 20, 0), Off with { DailyTimeIst = new TimeOnly(9, 0) }, Never).ShouldBeFalse();
        PortfolioRefreshSchedule.NextRun(Ist(10, 12, 10, 0), Off, Never).ShouldBeNull();
    }

    [Fact]
    public void Daily_mode_runs_once_after_the_time_and_not_again_that_day()
    {
        var attempt = new SyncActivity(Ist(10, 12, 16, 0, 5));

        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 15, 59, 59), Daily(), Never).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 16, 0), Daily(), Never).ShouldBeTrue();
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 16, 1), Daily(), attempt).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 23, 59), Daily(), attempt).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(Ist(10, 13, 15, 59), Daily(), attempt).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(Ist(10, 13, 16, 0), Daily(), attempt).ShouldBeTrue();
    }

    [Fact]
    public void Daily_mode_also_runs_on_weekends()
    {
        PortfolioRefreshSchedule.IsDue(Ist(10, 10, 16, 0), Daily(), Never).ShouldBeTrue(); // Saturday
        PortfolioRefreshSchedule.IsDue(Ist(10, 11, 16, 0), Daily(), Never).ShouldBeTrue(); // Sunday
    }

    [Fact]
    public void Daily_mode_catches_up_once_when_the_time_was_missed_that_day()
    {
        // The application was down at 16:00 and starts at 18:30; the last attempt was yesterday.
        var yesterday = new SyncActivity(Ist(10, 9, 16, 0));

        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 18, 30), Daily(), yesterday).ShouldBeTrue();
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 18, 31), Daily(), new SyncActivity(Ist(10, 12, 18, 30))).ShouldBeFalse();
    }

    [Fact]
    public void A_failed_or_skipped_attempt_counts_as_the_days_run_and_a_day_without_an_attempt_does_not()
    {
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 16, 30), Daily(), new SyncActivity(Ist(10, 12, 16, 0))).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 16, 30), Daily(), new SyncActivity(Ist(10, 12, 15, 0))).ShouldBeTrue(); // before the time: not today's run
        PortfolioRefreshSchedule.IsDue(Ist(10, 12, 16, 30), Daily(), Never).ShouldBeTrue();
    }

    [Fact]
    public void Daily_mode_judges_the_day_in_india_time()
    {
        // 2026-10-11 18:35 UTC is 2026-10-12 00:05 IST. The 00:00 schedule of the 12th has come, the 16:00 one has not.
        var justAfterMidnightIst = new DateTimeOffset(2026, 10, 11, 18, 35, 0, TimeSpan.Zero);
        var yesterdayEvening = new SyncActivity(Ist(10, 11, 16, 0));

        PortfolioRefreshSchedule.IsDue(justAfterMidnightIst, Daily(), yesterdayEvening).ShouldBeFalse();
        PortfolioRefreshSchedule.IsDue(justAfterMidnightIst, Daily(0, 0), yesterdayEvening).ShouldBeTrue();
    }

    [Fact]
    public void The_next_daily_run_is_today_at_the_time_now_when_it_was_missed_or_tomorrow()
    {
        var attempted = new SyncActivity(Ist(10, 12, 16, 0));

        PortfolioRefreshSchedule.NextRun(Ist(10, 12, 15, 0), Daily(), Never).ShouldBe(Ist(10, 12, 16, 0));
        PortfolioRefreshSchedule.NextRun(Ist(10, 12, 17, 0), Daily(), Never).ShouldBe(Ist(10, 12, 17, 0));
        PortfolioRefreshSchedule.NextRun(Ist(10, 12, 17, 0), Daily(), attempted).ShouldBe(Ist(10, 13, 16, 0));
    }
}
