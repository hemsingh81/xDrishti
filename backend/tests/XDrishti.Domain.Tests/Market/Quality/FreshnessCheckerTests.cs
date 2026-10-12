using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class FreshnessCheckerTests
{
    private static readonly TradingCalendar Plain = new();

    private static DateTimeOffset At(DateOnly date, int minute) => DateTimeOffset.FromUnixTimeSeconds(Epoch(date, minute));

    [Fact]
    public void An_instrument_with_the_last_closed_session_passes()
    {
        var check = FreshnessChecker.Check(Plain, Now, At(Fri, Minute(15, 29)));

        (check.Status, check.DaysBehind, check.ExpectedSession).ShouldBe((CheckStatus.Pass, 0, Fri));
        check.LastBar.ShouldBe(At(Fri, Minute(15, 29)));
        check.LastBar!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Fact]
    public void An_instrument_one_session_behind_fails_and_counts_the_trading_days()
    {
        var check = FreshnessChecker.Check(Plain, Now, At(Thu, Minute(15, 29)));

        (check.Status, check.DaysBehind).ShouldBe((CheckStatus.Fail, 1));
    }

    [Fact]
    public void A_holiday_on_the_last_weekday_does_not_make_an_instrument_behind()
    {
        var holiday = new TradingCalendar([TradingDay.Holiday(Fri, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);

        var check = FreshnessChecker.Check(holiday, Now, At(Thu, Minute(15, 29)));

        (check.Status, check.ExpectedSession, check.DaysBehind).ShouldBe((CheckStatus.Pass, Thu, 0));
    }

    [Fact]
    public void Today_counts_only_after_its_session_has_closed()
    {
        var beforeClose = new DateTimeOffset(2026, 10, 12, 15, 29, 0, TimeSpan.FromMinutes(330));
        var afterClose = new DateTimeOffset(2026, 10, 12, 15, 30, 0, TimeSpan.FromMinutes(330));

        FreshnessChecker.Check(Plain, beforeClose, At(Fri, Minute(15, 29))).Status.ShouldBe(CheckStatus.Pass);
        FreshnessChecker.Check(Plain, afterClose, At(Fri, Minute(15, 29))).Status.ShouldBe(CheckStatus.Fail);
    }

    [Fact]
    public void An_instrument_without_bars_fails()
    {
        var check = FreshnessChecker.Check(Plain, Now, null);

        (check.Status, check.LastBar).ShouldBe((CheckStatus.Fail, null));
    }
}
