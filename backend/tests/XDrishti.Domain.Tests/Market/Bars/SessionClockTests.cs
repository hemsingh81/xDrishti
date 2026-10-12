using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Market.Bars;

public sealed class SessionClockTests
{
    private static long At(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    [Fact]
    public void The_minute_of_the_day_and_the_date_are_in_india_time()
    {
        var late = At(2026, 3, 1, 23, 59);
        var early = At(2026, 3, 2, 0, 0);

        SessionClock.MinuteOfDay(late).ShouldBe(23 * 60 + 59);
        SessionClock.MinuteOfDay(early).ShouldBe(0);
        SessionClock.DateOf(late).ShouldBe(new DateOnly(2026, 3, 1));
        SessionClock.DateOf(early).ShouldBe(new DateOnly(2026, 3, 2));
        SessionClock.DateOf(0).ShouldBe(new DateOnly(1970, 1, 1));
        SessionClock.DateOf(-1).ShouldBe(new DateOnly(1970, 1, 1)); // 05:29:59 India
        SessionClock.DateOf(-19_801).ShouldBe(new DateOnly(1969, 12, 31));
    }

    [Theory]
    [InlineData(9, 14, false)]
    [InlineData(9, 15, true)]
    [InlineData(15, 29, true)]
    [InlineData(15, 30, false)]
    public void The_regular_session_is_0915_to_1529(int hour, int minute, bool inside)
    {
        SessionClock.InRegularSession(hour * 60 + minute).ShouldBe(inside);
    }

    [Theory]
    [InlineData(9, 14, false)]
    [InlineData(10, 0, true)]
    [InlineData(15, 30, false)]
    [InlineData(17, 29, false)]
    [InlineData(18, 15, true)]
    [InlineData(19, 59, true)]
    [InlineData(20, 0, false)]
    [InlineData(3, 0, false)]
    public void A_special_evening_session_counts_as_a_session_too(int hour, int minute, bool inside)
    {
        SessionClock.InAnySession(hour * 60 + minute).ShouldBe(inside);
    }

    [Fact]
    public void A_regular_session_has_375_bars()
    {
        SessionClock.RegularBars.ShouldBe(375);
    }
}
