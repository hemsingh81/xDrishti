using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// The one rule for the day a cash flow takes part in the benchmark (chart and XIRR alike): the first trading day on or after the flow's date, so a lot dated on a weekend or a holiday buys index units at the next session's close and a lot
/// bought and sold before a trading day comes never counts. Pure.
/// </summary>
public static class TradingDays
{
    /// <summary>How far ahead the next trading day is looked for before the date itself is used (a calendar full of holidays must not loop for ever).</summary>
    private const int LookaheadDays = 3660;

    public static DateOnly OnOrAfter(TradingCalendar calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        for (var i = 0; i <= LookaheadDays; i++)
        {
            var candidate = date.AddDays(i);
            if (calendar.IsTradingDay(candidate))
            {
                return candidate;
            }
        }

        return date;
    }
}
