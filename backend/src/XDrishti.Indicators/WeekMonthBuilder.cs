using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// The 1W and 1M part of <see cref="TimeframeBuilder"/> (design doc 05 §4, "bucket rules as built"). A week or a month is built <b>from the 1D bars of an inner day builder</b>, so the day rules — which bar closes a
/// day, what a complete day is, which late bar is ignored — are those of the 1D lane and exist once: open = the first day's open, high = the highest high, low = the lowest low, close = the last day's close,
/// volume = the sum, <c>Bars</c> = the sum of the days' 1-minute bars, <c>Complete</c> = every day that has bars is complete (the running period is not incomplete because it is not over). The bucket starts at India
/// midnight of the Monday of the week (a Saturday or Sunday session belongs to the Monday-to-Sunday week it falls in) or of the 1st of the month.
/// <para>
/// A period is closed (a) with a <see cref="TradingCalendar"/>, by the 1D bar of its <b>last session</b> — the greatest date of the period that is a trading day —, so at the very step that closes that day, and
/// (b) in every case by the first bar of a later period, if (a) did not apply: later, never earlier. Without a calendar only (b) is known: a holiday on the Friday or a Saturday special session would change what the
/// last session is. A bar of a period that has been emitted already (a special session the calendar did not know, or a date after the calendar's last session) is ignored and counted, as for a 1D bar after its close.
/// The bar that ends the last session at a later date of the same period (the session's last minute never came) closes the period and is itself ignored.
/// </para>
/// <para>
/// <b>Bad input does not throw.</b> The bounds of a week or month are computed with integers (<see cref="IndiaDays.MonthBounds"/>), so a bar whose date lies outside the range of <see cref="DateOnly"/> (years 1 to
/// 9999 — a feed that sends epoch milliseconds for seconds) is built like any other, as the 1D lane builds it. No calendar names such a date, so a period there has no known last session and is closed by the
/// first bar of a later period, as without a calendar.
/// </para>
/// </summary>
internal sealed class WeekMonthBuilder
{
    private const long NoDay = long.MinValue;

    private readonly Timeframe _timeframe;

    private readonly TradingCalendar? _calendar;

    private readonly TimeframeBuilder _days;

    private bool _active;

    private long _firstDay;

    private long _lastSession = NoDay;

    private double _open;

    private double _high;

    private double _low;

    private double _close;

    private long _volume;

    private int _bars;

    private bool _complete;

    private long _cachedFirst = long.MaxValue;

    private long _cachedNext = long.MinValue;

    private bool _emitted;

    private long _lastEmittedFirstDay;

    public WeekMonthBuilder(Timeframe timeframe, TradingCalendar? calendar)
    {
        _timeframe = timeframe;
        _calendar = calendar;
        _days = new TimeframeBuilder(Timeframe.D1, calendar);
    }

    public long Ignored { get; private set; }

    public int Push(in Bar minuteBar, out ClosedBar first, out ClosedBar second)
    {
        first = default;
        second = default;
        var barDay = IndiaDays.DayOf(minuteBar.EpochSeconds);
        var barFirstDay = FirstDayOf(barDay);
        if (_emitted && barFirstDay <= _lastEmittedFirstDay)
        {
            Ignored++;
            return 0;
        }

        var ignoredByDays = _days.Ignored;
        var daysClosed = _days.Push(in minuteBar, out var dayA, out var dayB);
        if (_days.Ignored != ignoredByDays)
        {
            // The day lane refused it (a bar at or before the previous one, or after the close of its own date): not part of any day, so not part of the period.
            Ignored++;
            return 0;
        }

        // A step closes at most two periods: the late one that a later period's first bar ends and the new one if its only session ends at once.
        var closed = 0;
        if (daysClosed > 0)
        {
            Take(in dayA, barDay, barFirstDay, ref closed, ref first, ref second);
        }

        if (daysClosed > 1)
        {
            Take(in dayB, barDay, barFirstDay, ref closed, ref first, ref second);
        }

        if (_active && _firstDay != barFirstDay)
        {
            // The first bar of a later period, and no session of the running one is left that could close it earlier.
            Place(Emit(), ref closed, ref first, ref second);
        }

        return closed;
    }

    public void Reset()
    {
        _days.Reset();
        _active = false;
        _emitted = false;
        _lastEmittedFirstDay = 0;
        Ignored = 0;
    }

    private static void Place(in ClosedBar bar, ref int closed, ref ClosedBar first, ref ClosedBar second)
    {
        if (closed == 0)
        {
            first = bar;
        }
        else
        {
            second = bar;
        }

        closed++;
    }

    /// <summary>Adds a day that the day lane has just closed to its period; closes the period if the day is its last session.</summary>
    private void Take(in ClosedBar day, long barDay, long barFirstDay, ref int closed, ref ClosedBar first, ref ClosedBar second)
    {
        var dayNumber = IndiaDays.DayOf(day.StartEpochSeconds);
        var firstDay = FirstDayOf(dayNumber);
        if (_emitted && firstDay <= _lastEmittedFirstDay)
        {
            // A day of a period that is over: the bar that began it was counted when it arrived.
            return;
        }

        if (_active && _firstDay != firstDay)
        {
            Place(Emit(), ref closed, ref first, ref second);
        }

        if (_active)
        {
            _high = Math.Max(_high, day.Bar.High);
            _low = Math.Min(_low, day.Bar.Low);
            _close = day.Bar.Close;
            _volume += day.Bar.Volume;
            _bars += day.Bars;
            _complete &= day.Complete;
        }
        else
        {
            Begin(firstDay, in day);
        }

        if (dayNumber == _lastSession)
        {
            if (barDay > dayNumber && barFirstDay == firstDay)
            {
                // The last session's own last minute never came and this bar, of a later date of the same period, ended it: the bar is not in the period.
                Ignored++;
            }

            Place(Emit(), ref closed, ref first, ref second);
        }
    }

    private void Begin(long firstDay, in ClosedBar day)
    {
        _active = true;
        _firstDay = firstDay;
        _open = day.Bar.Open;
        _high = day.Bar.High;
        _low = day.Bar.Low;
        _close = day.Bar.Close;
        _volume = day.Bar.Volume;
        _bars = day.Bars;
        _complete = day.Complete;
        _lastSession = _calendar is { } calendar ? LastSessionOf(calendar, firstDay) : NoDay;
    }

    private ClosedBar Emit()
    {
        _active = false;
        _emitted = true;
        _lastEmittedFirstDay = _firstDay;
        var start = IndiaDays.MidnightOf(_firstDay);
        return new ClosedBar(start, new Bar(start, _open, _high, _low, _close, _volume), _bars, _complete, _timeframe);
    }

    /// <summary>
    /// The greatest day of the period (given by its first day) that is a trading day; <see cref="NoDay"/> when the calendar has none in it. A day outside the range of <see cref="DateOnly"/> (a garbage epoch) is in no
    /// calendar: such a period has no known last session and is closed by the first bar of a later one, as without a calendar.
    /// </summary>
    private long LastSessionOf(TradingCalendar calendar, long firstDay)
    {
        Bounds(firstDay, out _, out var next);
        for (var day = next - 1; day >= firstDay; day--)
        {
            if (IndiaDays.TryDateOf(day, out var date) && calendar.IsTradingDay(date))
            {
                return day;
            }
        }

        return NoDay;
    }

    /// <summary>The first day of the period that holds the day; the bounds of the last period asked for are kept, so a bar of the same period costs two comparisons.</summary>
    private long FirstDayOf(long day)
    {
        if (day < _cachedFirst || day >= _cachedNext)
        {
            Bounds(day, out _cachedFirst, out _cachedNext);
        }

        return _cachedFirst;
    }

    /// <summary>The first day of the period of the day (a Monday, or the 1st) and the first day after it, as days since 1970-01-01 in India time; integers only, so it exists for every day, also outside the range of <see cref="DateOnly"/>.</summary>
    private void Bounds(long day, out long first, out long next)
    {
        if (_timeframe == Timeframe.W1)
        {
            first = day - IndiaDays.DaysSinceMonday(day);
            next = first + 7;
            return;
        }

        IndiaDays.MonthBounds(day, out first, out next);
    }
}
