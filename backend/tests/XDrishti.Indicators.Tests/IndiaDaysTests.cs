using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The integer India-calendar arithmetic agrees with the Domain's <c>IndiaTime</c> and <c>SessionClock</c>.</summary>
public sealed class IndiaDaysTests
{
    [Fact]
    public void The_offset_is_the_india_time_offset()
    {
        IndiaDays.OffsetSeconds.ShouldBe((long)IndiaTime.Offset.TotalSeconds);
    }

    [Fact]
    public void Day_and_minute_of_day_agree_with_the_domain_for_instants_around_midnight_and_the_session()
    {
        var random = new Random(3);
        for (var i = 0; i < 20_000; i++)
        {
            var epoch = random.NextInt64(-2_000_000_000, 4_000_000_000);
            epoch -= epoch % 60;

            var date = SessionClock.DateOf(epoch);

            IndiaDays.MinuteOfDay(epoch).ShouldBe(SessionClock.MinuteOfDay(epoch));
            DateOnly.FromDayNumber((int)IndiaDays.DayOf(epoch) + 719_162).ShouldBe(date);
            IndiaDays.MidnightOf(IndiaDays.DayOf(epoch)).ShouldBeLessThanOrEqualTo(epoch);
        }
    }

    [Fact]
    public void Eighteen_thirty_utc_is_the_next_india_date_and_midnight_india_is_the_start_of_it()
    {
        var epoch = Minutes.Epoch(new DateOnly(2026, 3, 2), 0);

        IndiaDays.MinuteOfDay(epoch).ShouldBe(0);
        IndiaDays.MinuteOfDay(epoch - 60).ShouldBe(24 * 60 - 1);
        IndiaDays.DayOf(epoch).ShouldBe(IndiaDays.DayOf(epoch - 60) + 1);
        IndiaDays.MidnightOf(IndiaDays.DayOf(epoch)).ShouldBe(epoch);
    }

    [Fact]
    public void The_epoch_day_number_is_that_of_1970_01_01_and_day_numbers_convert_both_ways()
    {
        IndiaDays.UnixEpochDayNumber.ShouldBe(new DateOnly(1970, 1, 1).DayNumber);
        IndiaDays.DayNumberOf(new DateOnly(1970, 1, 1)).ShouldBe(0);
        IndiaDays.DayNumberOf(new DateOnly(1969, 12, 31)).ShouldBe(-1);
        IndiaDays.DayNumberOf(DateOnly.MinValue).ShouldBe(-IndiaDays.UnixEpochDayNumber);

        foreach (var date in new[] { DateOnly.MinValue, new DateOnly(1600, 2, 29), new DateOnly(1970, 1, 1), new DateOnly(2026, 3, 2), new DateOnly(2400, 2, 29), DateOnly.MaxValue })
        {
            IndiaDays.TryDateOf(IndiaDays.DayNumberOf(date), out var back).ShouldBeTrue();
            back.ShouldBe(date);
        }
    }

    [Fact]
    public void A_day_outside_years_1_to_9999_has_no_date_and_the_conversion_does_not_throw()
    {
        var first = -IndiaDays.UnixEpochDayNumber;
        var last = DateOnly.MaxValue.DayNumber - IndiaDays.UnixEpochDayNumber;

        IndiaDays.TryDateOf(first, out _).ShouldBeTrue();
        IndiaDays.TryDateOf(first - 1, out _).ShouldBeFalse("the day before 0001-01-01");
        IndiaDays.TryDateOf(last, out _).ShouldBeTrue();
        IndiaDays.TryDateOf(last + 1, out _).ShouldBeFalse("the day after 9999-12-31");
        foreach (var day in new[] { long.MinValue, long.MinValue + 1, -4_629_629_000_000L, 4_629_629L, 4_629_629_000_000L, long.MaxValue - 719_162, long.MaxValue - 1, long.MaxValue })
        {
            IndiaDays.TryDateOf(day, out var date).ShouldBe(day >= first && day <= last, day.ToString());
            date.ShouldBe(default);
        }
    }

    [Fact]
    public void The_days_since_the_monday_agree_with_the_platform_weekday_for_every_day_of_the_range()
    {
        var wrong = new List<DateOnly>();

        for (var number = 0; number <= DateOnly.MaxValue.DayNumber; number++)
        {
            var date = DateOnly.FromDayNumber(number);
            if (IndiaDays.DaysSinceMonday(number - IndiaDays.UnixEpochDayNumber) != ((int)date.DayOfWeek + 6) % 7)
            {
                wrong.Add(date);
            }
        }

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public void The_month_bounds_agree_with_the_platform_calendar_for_every_day_of_years_1_to_9999()
    {
        var wrong = new List<DateOnly>();

        for (var number = 0; number <= DateOnly.MaxValue.DayNumber; number++)
        {
            var date = DateOnly.FromDayNumber(number);
            var firstOfMonth = new DateOnly(date.Year, date.Month, 1);
            var nextFirst = date.Year == 9999 && date.Month == 12 ? DateOnly.MaxValue.DayNumber + 1 : firstOfMonth.AddMonths(1).DayNumber;

            IndiaDays.MonthBounds(number - IndiaDays.UnixEpochDayNumber, out var first, out var next);

            if (first != firstOfMonth.DayNumber - IndiaDays.UnixEpochDayNumber || next != nextFirst - IndiaDays.UnixEpochDayNumber)
            {
                wrong.Add(date);
            }
        }

        wrong.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-1_000_000_000_000L)]
    [InlineData(-4_629_629L)]
    [InlineData(4_629_629L)]
    [InlineData(1_000_000_000_000L)]
    public void Beyond_the_range_the_months_follow_the_same_rules_and_repeat_every_400_years(long farDay)
    {
        const long Cycle = 146_097;
        var inside = IndiaDays.FloorMod(farDay, Cycle);
        var shift = farDay - inside;

        for (var offset = 0; offset < 3 * 366; offset++)
        {
            IndiaDays.MonthBounds(farDay + offset, out var first, out var next);
            IndiaDays.MonthBounds(inside + offset, out var nearFirst, out var nearNext);

            first.ShouldBe(nearFirst + shift);
            next.ShouldBe(nearNext + shift);
            (farDay + offset).ShouldBeInRange(first, next - 1);
            (next - first).ShouldBeInRange(28, 31);
        }
    }

    [Fact]
    public void The_months_of_a_leap_century_year_and_of_a_year_that_is_not_leap_have_their_real_lengths()
    {
        long Length(int year, int month)
        {
            IndiaDays.MonthBounds(IndiaDays.DayNumberOf(new DateOnly(year, month, 10)), out var first, out var next);
            return next - first;
        }

        Length(2000, 2).ShouldBe(29, "divisible by 400");
        Length(1900, 2).ShouldBe(28, "divisible by 100 but not by 400");
        Length(2100, 2).ShouldBe(28);
        Length(2028, 2).ShouldBe(29);
        Length(2027, 2).ShouldBe(28);
        Length(2026, 12).ShouldBe(31);
        Length(9999, 12).ShouldBe(31);
        Length(1, 1).ShouldBe(31);
    }
}
