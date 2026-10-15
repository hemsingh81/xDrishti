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
}
