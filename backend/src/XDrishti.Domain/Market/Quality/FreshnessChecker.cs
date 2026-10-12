using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Market.Quality;

/// <summary>Freshness against the trading calendar: the last bar must be on or after the last session that has closed; every trading day it is behind counts. No bars at all fails.</summary>
public static class FreshnessChecker
{
    public static FreshnessCheck Check(TradingCalendar calendar, DateTimeOffset now, DateTimeOffset? lastBar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var expected = calendar.LastClosedSession(now);
        if (lastBar is not { } last)
        {
            return new FreshnessCheck(CheckStatus.Fail, null, expected, 0);
        }

        var behind = calendar.TradingDaysBetween(IndiaTime.Today(last), expected);
        return new FreshnessCheck(behind == 0 ? CheckStatus.Pass : CheckStatus.Fail, IndiaTime.ToIst(last), expected, behind);
    }
}
