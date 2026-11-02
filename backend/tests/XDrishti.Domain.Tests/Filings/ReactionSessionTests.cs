using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-05 criterion 2 (the reaction session): the first session whose open is at or after broadcast + lag, on the hand examples of the spec, a session that opens late and a holiday.</summary>
public sealed class ReactionSessionTests
{
    private static readonly TradingCalendar Plain = new();

    private static DateTimeOffset July(int day, int hour, int minute) => new(2026, 7, day, hour, minute, 0, Ist);

    [Fact]
    public void A_friday_evening_broadcast_reacts_on_monday_and_a_monday_morning_one_on_tuesday()
    {
        ReactionSession.Of(July(24, 16, 12), Lag, Plain).ShouldBe(new DateOnly(2026, 7, 27));
        ReactionSession.Of(July(27, 11, 0), Lag, Plain).ShouldBe(new DateOnly(2026, 7, 28));
    }

    [Theory]
    [InlineData(8, 0, 5, 27)]   // before the open: the same day
    [InlineData(9, 10, 5, 27)]  // 09:10 + 5 minutes is exactly the open: the open is at or after
    [InlineData(9, 11, 5, 28)]  // one minute later: the next session
    [InlineData(9, 15, 0, 27)]
    [InlineData(9, 16, 0, 28)]
    public void The_open_is_inclusive_and_the_lag_counts(int hour, int minute, int lagMinutes, int reactsOn)
    {
        ReactionSession.Of(July(27, hour, minute), TimeSpan.FromMinutes(lagMinutes), Plain).ShouldBe(new DateOnly(2026, 7, reactsOn));
    }

    [Fact]
    public void A_holiday_is_skipped_and_the_close_is_the_close_of_the_session()
    {
        var holiday = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 7, 27), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

        ReactionSession.Of(July(24, 16, 12), Lag, holiday).ShouldBe(new DateOnly(2026, 7, 28));
        ReactionSession.CloseOf(new DateOnly(2026, 7, 28), holiday).ShouldBe(new DateTimeOffset(2026, 7, 28, 15, 30, 0, Ist));
        ReactionSession.CloseOf(new DateOnly(2026, 7, 27), holiday).ShouldBeNull();
    }

    [Fact]
    public void A_special_session_opens_and_closes_at_its_own_times()
    {
        var saturday = new DateOnly(2026, 7, 25);
        var special = new TradingCalendar([TradingDay.Special(saturday, 17 * 60, 18 * 60, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

        ReactionSession.Of(July(24, 16, 12), Lag, special).ShouldBe(saturday);
        ReactionSession.CloseOf(saturday, special).ShouldBe(new DateTimeOffset(2026, 7, 25, 18, 0, 0, Ist));
    }
}
