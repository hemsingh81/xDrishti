using System.Globalization;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Baskets.Derived;

/// <summary>
/// When a derived basket is due and from when its version counts (BK-03). Everything comes from the trading calendar, never from a fixed weekday offset: a version computed
/// from the bars of <c>as_of</c> takes effect on the next session, so a consumer deciding on a day never sees bars it could not know.
/// </summary>
public static class DerivedSchedule
{
    /// <summary>The minutes of the opening window basket 4 reads (09:15 to 09:20).</summary>
    public const int GapperMinutes = 5;

    /// <summary>How many minutes after the 09:15 open a frozen intraday basket is formed (basket 4: 5, basket 12: its opening-range minutes); null for a basket that is not formed intraday.</summary>
    public static int? OpeningMinutes(DerivedDefinition definition, DerivedParams parameters)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(parameters);
        return definition.Cadence != DerivedCadence.FrozenIntraday ? null : definition.Id == 4 ? GapperMinutes : parameters.Whole("Basket12.OrMinutes");
    }

    /// <summary>How far ahead the next session is searched (a calendar full of holidays must not loop for ever).</summary>
    private const int Lookahead = 40;

    /// <summary>The first session after <paramref name="asOf"/>.</summary>
    public static DateOnly NextSession(TradingCalendar calendar, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        for (var days = 1; days <= Lookahead; days++)
        {
            var date = asOf.AddDays(days);
            if (calendar.IsTradingDay(date))
            {
                return date;
            }
        }

        return asOf.AddDays(Lookahead);
    }

    /// <summary>
    /// Whether a basket of this cadence is formed from the bars of <paramref name="asOf"/>: daily on every session, weekly when the next session lies in another ISO week (the last session of the week, whatever day a holiday moves it to),
    /// monthly when it lies in another month. A frozen intraday basket is never due here (it is formed from live minute bars, which this build does not have).
    /// </summary>
    public static bool IsDue(DerivedCadence cadence, TradingCalendar calendar, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (!calendar.IsTradingDay(asOf))
        {
            return false;
        }

        var next = NextSession(calendar, asOf);
        return cadence switch
        {
            DerivedCadence.Daily => true,
            DerivedCadence.Weekly => WeekOf(next) != WeekOf(asOf),
            DerivedCadence.Monthly => next.Month != asOf.Month || next.Year != asOf.Year,
            _ => false,
        };
    }

    /// <summary>The first date after <paramref name="after"/> that is due for the cadence.</summary>
    public static DateOnly NextDue(DerivedCadence cadence, TradingCalendar calendar, DateOnly after)
    {
        for (var days = 1; days <= 400; days++)
        {
            var date = after.AddDays(days);
            if (IsDue(cadence, calendar, date))
            {
                return date;
            }
        }

        return after.AddDays(400);
    }

    /// <summary>Why a basket cannot be formed by this build; null when it can. Basket 13 reads BK-05's dated lists (a missing flag is not a pass: until they are recorded every share is excluded); the intraday ones need live minute bars that are not recorded.</summary>
    public static string? WhyNotRunnable(DerivedDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.NeedsIntraday ? "intraday_unavailable" : null;
    }

    private static (int Year, int Week) WeekOf(DateOnly date)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day));
    }
}
