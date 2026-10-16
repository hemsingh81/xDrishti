using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>A calendar as the frame is given it (null: none) and the same facts for the independent reference (null sessions: no calendar).</summary>
internal sealed record KnownCalendar(TradingCalendar? Calendar, IReadOnlyDictionary<DateOnly, (int From, int To)>? Sessions, IReadOnlySet<DateOnly> Holidays)
{
    public FrameOptions Options(int historyDepth = FrameOptions.DefaultHistoryDepth) => new(Calendar, historyDepth);

    /// <summary>The same facts as the grouping reference reads them, written without <c>TradingCalendar</c> (null when no calendar is known).</summary>
    public ReferenceCalendar? Reference => Sessions is null ? null : new ReferenceCalendar(Sessions, Holidays);
}

/// <summary>The calendars of the weekly and monthly frame tests, built from a series and its holidays.</summary>
internal static class PeriodCalendars
{
    private static readonly Dictionary<DateOnly, (int From, int To)> NoSessions = [];

    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    public static KnownCalendar For(PeriodSeries series, CalendarKnowledge kind)
    {
        switch (kind)
        {
            case CalendarKnowledge.None:
                return new KnownCalendar(null, null, NoHolidays);
            case CalendarKnowledge.Full:
                var windows = CalendarFixtures.Windows(series.Bars);
                return new KnownCalendar(CalendarFixtures.For(windows, series.Holidays), windows, series.Holidays);
            case CalendarKnowledge.WeekdaysOnly:
                var weekdays = CalendarFixtures.WeekdayWindows(series.Bars);
                return new KnownCalendar(CalendarFixtures.For(weekdays, series.Holidays), weekdays, series.Holidays);
            default:
                return new KnownCalendar(CalendarFixtures.For(NoSessions, series.Holidays), NoSessions, series.Holidays);
        }
    }
}
