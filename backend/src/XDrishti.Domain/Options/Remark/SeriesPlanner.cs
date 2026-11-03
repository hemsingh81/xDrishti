using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// Which rolling series a trade is re-marked on (spec P14-08, expiry rule <c>nearest_safe</c>): NIFTY on the weekly series, BANKNIFTY on the weekly series until its weeklies were withdrawn and on the monthly series after, shares on the monthly series, always the
/// nearest (code 1) unless the trade's planned last session is on or after the last session before that expiry, then code 2. The backfill holds NIFTY W1, W2, M1; BANKNIFTY M1, M2 and W1 (until the withdrawal); shares M1 only, at 15 minutes, so a share hold that
/// reaches its expiry session has <c>no_series_for_expiry</c>. Pure.
/// </summary>
public static class SeriesPlanner
{
    public const int IndexInterval = 5;
    public const int ShareInterval = 15;

    /// <summary>The planned last session: the trade date for a sell (intraday only) and for a one-session buy, else the entry session counted as 1 plus the sessions to hold.</summary>
    public static DateOnly PlannedLastSession(TradingCalendar calendar, DateOnly tradeDate, bool isSell, int maxHoldSessions)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var date = tradeDate;
        for (var held = 1; !isSell && held < maxHoldSessions; held++)
        {
            do
            {
                date = date.AddDays(1);
            }
            while (!calendar.IsTradingDay(date));
        }

        return date;
    }

    public static SeriesPlanResult Plan(string canonicalSymbol, DateOnly tradeDate, DateOnly plannedLast, ExpiryCalendar expiries, IEnumerable<MarketEpoch> epochs)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        ArgumentNullException.ThrowIfNull(expiries);
        ArgumentNullException.ThrowIfNull(epochs);
        var isIndex = canonicalSymbol is ExpiryRules.Nifty or ExpiryRules.BankNifty;
        var weeklyEnd = ExpiryRules.BankNiftyWeeklyEnd(epochs);
        // BANKNIFTY is on the weekly only while a weekly expiry exists inside a weekly era (the derived rule ends the day before the withdrawal epoch), else on the monthly.
        var flag = canonicalSymbol switch
        {
            ExpiryRules.Nifty => 'W',
            ExpiryRules.BankNifty => tradeDate <= weeklyEnd && expiries.Nth('W', tradeDate, 1) is not null ? 'W' : 'M',
            _ => 'M',
        };
        var interval = isIndex ? IndexInterval : ShareInterval;
        var nearest = expiries.Nth(flag, tradeDate, 1);
        if (nearest is null || expiries.LastSessionBefore(nearest.Date) is not { } lastBefore)
        {
            return new SeriesPlanResult(null, RemarkFates.ExpiryUnknown);
        }

        if (plannedLast < lastBefore)
        {
            return new SeriesPlanResult(new SeriesPlan(flag, 1, nearest, interval, lastBefore), null);
        }

        if (!Held(canonicalSymbol, flag, 2, tradeDate, weeklyEnd))
        {
            return new SeriesPlanResult(null, RemarkFates.NoSeriesForExpiry);
        }

        return expiries.Nth(flag, tradeDate, 2) is { } next && expiries.LastSessionBefore(next.Date) is { } nextLast
            ? new SeriesPlanResult(new SeriesPlan(flag, 2, next, interval, nextLast), null)
            : new SeriesPlanResult(null, RemarkFates.ExpiryUnknown);
    }

    /// <summary>Whether the backfill holds the series (kind and code) for the underlying on the date (ADR 0177 decision 3).</summary>
    private static bool Held(string symbol, char flag, int code, DateOnly tradeDate, DateOnly weeklyEnd) => symbol switch
    {
        ExpiryRules.Nifty => (flag == 'W' && code <= 2) || (flag == 'M' && code == 1),
        ExpiryRules.BankNifty => (flag == 'M' && code <= 2) || (flag == 'W' && code == 1 && tradeDate <= weeklyEnd),
        _ => flag == 'M' && code == 1,
    };
}
