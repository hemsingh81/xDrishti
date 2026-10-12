using XDrishti.Infrastructure.Market.Bars;

namespace XDrishti.Infrastructure.Tests.Market.Bars;

public sealed class IstBucketsTests
{
    private static DateTimeOffset India(int year, int month, int day, int hour = 0, int minute = 0) => new(year, month, day, hour, minute, 0, TimeSpan.FromMinutes(330));

    [Fact]
    public void A_day_starts_at_india_midnight_which_is_the_evening_before_in_utc()
    {
        IstBuckets.DayStart(India(2026, 3, 3, 9, 15)).ShouldBe(India(2026, 3, 3));
        IstBuckets.DayStart(India(2026, 3, 3, 23, 59)).ShouldBe(India(2026, 3, 3));
        IstBuckets.DayStart(India(2026, 3, 3)).ShouldBe(India(2026, 3, 3));
        IstBuckets.DayStart(India(2026, 3, 3, 0, 0).ToUniversalTime()).UtcDateTime.ShouldBe(new DateTime(2026, 3, 2, 18, 30, 0, DateTimeKind.Utc));
        IstBuckets.DayStart(new DateTimeOffset(2026, 3, 2, 18, 29, 0, TimeSpan.Zero)).ShouldBe(India(2026, 3, 2));
        IstBuckets.DayStart(new DateTimeOffset(2026, 3, 2, 18, 30, 0, TimeSpan.Zero)).ShouldBe(India(2026, 3, 3));
    }

    [Theory]
    [InlineData(2026, 3, 2, 2026, 3, 2)]   // Monday
    [InlineData(2026, 3, 4, 2026, 3, 2)]
    [InlineData(2026, 3, 8, 2026, 3, 2)]   // Sunday belongs to the week of the Monday before
    [InlineData(2026, 3, 1, 2026, 2, 23)]
    [InlineData(2026, 1, 1, 2025, 12, 29)] // across a year
    public void A_week_starts_on_monday(int y, int m, int d, int ey, int em, int ed)
    {
        IstBuckets.WeekStart(India(y, m, d, 12)).ShouldBe(India(ey, em, ed));
    }

    [Theory]
    [InlineData(2026, 2, 28, 2026, 2, 1, 2026, 3, 1)]
    [InlineData(2026, 3, 1, 2026, 3, 1, 2026, 4, 1)]
    [InlineData(2026, 12, 31, 2026, 12, 1, 2027, 1, 1)]
    public void A_month_runs_from_the_first_to_the_first_of_the_next_month(int y, int m, int d, int sy, int sm, int sd, int ny, int nm, int nd)
    {
        IstBuckets.MonthStart(India(y, m, d, 20)).ShouldBe(India(sy, sm, sd));
        IstBuckets.NextMonthStart(India(y, m, d, 20)).ShouldBe(India(ny, nm, nd));
    }

    [Fact]
    public void A_late_evening_in_india_is_still_the_same_month_although_utc_is_the_day_before_and_the_other_way_round()
    {
        // 2026-03-01 00:10 India is 2026-02-28 18:40 UTC: February in UTC, March in India.
        var instant = new DateTimeOffset(2026, 2, 28, 18, 40, 0, TimeSpan.Zero);

        IstBuckets.MonthStart(instant).ShouldBe(India(2026, 3, 1));
        IstBuckets.DayStart(instant).ShouldBe(India(2026, 3, 1));
        IstBuckets.WeekStart(instant).ShouldBe(India(2026, 2, 23));
    }
}
