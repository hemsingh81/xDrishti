using XDrishti.Application.Market.Refresh;

namespace XDrishti.Application.Tests.Market;

/// <summary>The daily run is a fixed time of day in India (UTC+05:30, no daylight saving time).</summary>
public sealed class RefreshScheduleTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, TimeSpan.Zero);

    private static DateTimeOffset At(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, Ist);

    [Fact]
    public void Before_the_time_the_run_is_today()
    {
        // 02:00 UTC = 07:30 IST
        RefreshSchedule.NextRun(Utc(2026, 10, 11, 2, 0), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 11, 8, 45));
    }

    [Fact]
    public void After_the_time_the_run_is_tomorrow()
    {
        // 04:00 UTC = 09:30 IST
        RefreshSchedule.NextRun(Utc(2026, 10, 11, 4, 0), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 12, 8, 45));
    }

    [Fact]
    public void At_exactly_the_time_the_next_run_is_tomorrow()
    {
        RefreshSchedule.NextRun(At(2026, 10, 11, 8, 45), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 12, 8, 45));
    }

    [Fact]
    public void One_second_before_the_time_the_run_is_today()
    {
        RefreshSchedule.NextRun(At(2026, 10, 11, 8, 44).AddSeconds(59), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 11, 8, 45));
    }

    [Fact]
    public void India_is_already_on_the_next_day_while_UTC_is_still_on_the_previous_one()
    {
        // 2026-10-11 20:00 UTC = 2026-10-12 01:30 IST: today (in India) is the 12th, 08:45 is ahead.
        RefreshSchedule.NextRun(Utc(2026, 10, 11, 20, 0), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 12, 8, 45));
    }

    [Fact]
    public void A_time_early_in_the_India_day_falls_on_the_previous_UTC_date()
    {
        // 00:10 IST is 18:40 UTC of the day before.
        var next = RefreshSchedule.NextRun(Utc(2026, 10, 11, 12, 0), new TimeOnly(0, 10));

        next.ShouldBe(At(2026, 10, 12, 0, 10));
        next.UtcDateTime.ShouldBe(new DateTime(2026, 10, 11, 18, 40, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void The_result_is_the_same_instant_whatever_offset_the_clock_value_has()
    {
        var utc = Utc(2026, 10, 11, 2, 0);

        RefreshSchedule.NextRun(utc.ToOffset(TimeSpan.FromHours(-8)), new TimeOnly(8, 45)).ShouldBe(RefreshSchedule.NextRun(utc, new TimeOnly(8, 45)));
    }

    [Theory]
    [InlineData(2026, 12, 31, 5, 0, 2027, 1, 1)] // year end
    [InlineData(2026, 2, 28, 5, 0, 2026, 3, 1)] // month end, not a leap year
    [InlineData(2028, 2, 28, 5, 0, 2028, 2, 29)] // leap day
    public void The_run_rolls_over_month_and_year_ends(int y, int mo, int d, int h, int mi, int ny, int nmo, int nd) =>
        RefreshSchedule.NextRun(Utc(y, mo, d, h, mi), new TimeOnly(8, 45)).ShouldBe(At(ny, nmo, nd, 8, 45));

    [Fact]
    public void There_is_no_daylight_saving_shift_around_the_dates_other_zones_change()
    {
        // The same IST time on both sides of the European and US clock changes.
        foreach (var (y, mo, d) in new[] { (2026, 3, 28), (2026, 3, 29), (2026, 10, 24), (2026, 10, 25), (2026, 11, 1), (2026, 3, 8) })
        {
            var next = RefreshSchedule.NextRun(At(y, mo, d, 9, 0), new TimeOnly(8, 45));

            next.ShouldBe(At(y, mo, d, 8, 45).AddDays(1));
            next.Offset.ShouldBe(Ist);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(23, 59)]
    public void The_edges_of_the_day_work(int hour, int minute)
    {
        var now = At(2026, 10, 11, 12, 0);

        RefreshSchedule.NextRun(now, new TimeOnly(hour, minute)).ShouldBe(At(2026, 10, 11, hour, minute).AddDays(hour == 0 ? 1 : 0));
    }

    [Fact]
    public void The_previous_run_is_today_once_the_time_has_come_and_yesterday_before_it()
    {
        RefreshSchedule.PreviousRun(At(2026, 10, 11, 9, 0), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 11, 8, 45));
        RefreshSchedule.PreviousRun(At(2026, 10, 11, 8, 44), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 10, 8, 45));
        RefreshSchedule.PreviousRun(At(2026, 10, 11, 8, 45), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 11, 8, 45));
    }

    [Fact]
    public void The_previous_run_follows_the_India_date_not_the_UTC_date()
    {
        // 2026-10-11 20:00 UTC = 2026-10-12 01:30 IST: the last 08:45 was the 11th (03:15 UTC).
        RefreshSchedule.PreviousRun(Utc(2026, 10, 11, 20, 0), new TimeOnly(8, 45)).ShouldBe(At(2026, 10, 11, 8, 45));
    }

    [Theory]
    [InlineData(2027, 1, 1, 2027, 1, 1)]
    [InlineData(2027, 3, 1, 2027, 3, 1)]
    public void The_previous_run_rolls_back_over_month_and_year_ends(int y, int mo, int d, int py, int pmo, int pd) =>
        RefreshSchedule.PreviousRun(At(y, mo, d, 9, 0), new TimeOnly(8, 45)).ShouldBe(At(py, pmo, pd, 8, 45));

    [Fact]
    public void The_previous_and_next_run_are_a_day_apart_around_every_time()
    {
        var now = At(2026, 3, 1, 4, 0);

        (RefreshSchedule.NextRun(now, new TimeOnly(8, 45)) - RefreshSchedule.PreviousRun(now, new TimeOnly(8, 45))).ShouldBe(TimeSpan.FromDays(1));
    }
}
