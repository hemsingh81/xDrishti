using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// Builds <b>closed</b> 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M bars from the 1-minute stream with exactly the rules of the stored aggregates (design doc 05 §4, <c>BarAggregates</c>): intraday buckets are anchored to the
/// 09:15 India open, the last buckets are clipped at the 15:30 close (the 1h bucket at 15:15 holds 15 bars, the 10m bucket at 15:25 five), the day is the India date, the week starts at India midnight of its Monday (a
/// Saturday or Sunday session belongs to the Monday-to-Sunday week it falls in) and the month at midnight of the 1st; open = first, high = max, low = min, close = last, volume = sum, <c>Bars</c> the number of 1-minute bars used,
/// <c>Complete</c> as stored (an intraday bucket holds exactly the minutes of its window; a day has all 375 regular minutes, or, with none of them, every minute between its first and last bar; a week or a month is complete
/// when every day that has bars is — the running period is not incomplete because it is not over).
/// <para>
/// A bucket is emitted when the bar that ends its window has been fed (the 15:29 bar for a bucket clipped at the close and for a regular day), or, if that minute never arrives, by the first bar of a later bucket —
/// flagged <c>Complete = false</c>. Given a <see cref="TradingCalendar"/>, the last minute of a special session also ends its buckets and its day. A bucket is emitted exactly once and never while it is forming.
/// Bars must come in time order: a bar at or before the previous one, or for a bucket already emitted, is ignored and counted in <see cref="Ignored"/>. Thread-confined; allocates nothing after construction.
/// </para>
/// <para>
/// <b>1W and 1M.</b> They are built from the days of the 1D rules above, and are emitted (a) with a calendar, at the step that closes the <b>last session of the period</b> — the greatest date of the week or month for which
/// <c>IsTradingDay</c> is true: the Thursday when the Friday is a holiday, the last weekday of a month that ends on a weekend, the Saturday of a special Saturday session; it is the same step at which the 1D bar of that date
/// is emitted — and (b) in every case, if (a) did not close it, by the first bar of a later week or month. Without a calendar the builder cannot know that a Friday is the last session (a holiday, or a Saturday session,
/// would change it), so the bar comes at (b): later than with a calendar, never earlier. A bar of a week or month that has been emitted already — a special session the calendar did not know, or a date after the
/// calendar's last session of the period — is ignored and counted in <see cref="Ignored"/>, as an evening bar after a regular 1D close is. A period without a bar is never emitted.
/// </para>
/// <para>
/// <b>Differences from the stored 1W and 1M.</b> The stored buckets merge every bar of the period, these are built from the days the 1D rules accept, so a bar that the 1D lane ignores or that comes after the period was
/// emitted is not in them (it is in <see cref="Ignored"/>); a period is emitted when it is over, not while it runs; and a stream's last period is not emitted (there is no flush). A stream that starts in the middle of a
/// period gives that period only the days from its first bar. To compare with stored data, pass the same <see cref="TradingCalendar"/>.
/// </para>
/// </summary>
public sealed class TimeframeBuilder
{
    private const long DaySeconds = IndiaDays.SecondsPerDay;

    private readonly Timeframe _timeframe;

    private readonly int _minutes;

    private readonly TradingCalendar? _calendar;

    private bool _active;

    private long _start;

    private double _open;

    private double _high;

    private double _low;

    private double _close;

    private long _volume;

    private int _bars;

    private int _regularBars;

    private long _firstEpoch;

    private long _lastEpoch;

    private int _windowEnd;

    private bool _seen;

    private long _lastSeen;

    private bool _emitted;

    private long _lastEmittedStart;

    private long _ignored;

    private readonly WeekMonthBuilder? _periods;

    /// <param name="timeframe">5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M.</param>
    /// <param name="calendar">Optional: lets a special session close its last bucket and its day at its own last minute instead of waiting for the next bucket, and lets a week or a month close at its last session instead of at
    /// the first bar of the next one. Without it only the regular close is known.</param>
    public TimeframeBuilder(Timeframe timeframe, TradingCalendar? calendar = null)
    {
        if (timeframe is not (Timeframe.M5 or Timeframe.M10 or Timeframe.M15 or Timeframe.M30 or Timeframe.H1 or Timeframe.D1 or Timeframe.W1 or Timeframe.Mo1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Only 5m, 10m, 15m, 30m, 1h, 1D, 1W and 1M are built.");
        }

        _timeframe = timeframe;
        _minutes = timeframe.Minutes();
        _calendar = calendar;
        if (timeframe is Timeframe.W1 or Timeframe.Mo1)
        {
            _periods = new WeekMonthBuilder(timeframe, calendar);
        }
    }

    public Timeframe Timeframe => _timeframe;

    /// <summary>How many bars were ignored because they were out of order or belonged to a bucket that had been emitted already (0 on valid data).</summary>
    public long Ignored => _periods?.Ignored ?? _ignored;

    /// <summary>
    /// Feeds the next 1-minute bar and returns how many buckets it closed (0, 1 or 2): a bar can close the previous bucket (a later bucket began) and, if it is the last minute of its own window and the first of its
    /// bucket, close that one too. The first closed bucket is in <paramref name="first"/>, the second in <paramref name="second"/>.
    /// </summary>
    public int Push(in Bar minuteBar, out ClosedBar first, out ClosedBar second)
    {
        if (_periods is not null)
        {
            return _periods.Push(in minuteBar, out first, out second);
        }

        first = default;
        second = default;
        var epoch = minuteBar.EpochSeconds;
        if (_seen && epoch <= _lastSeen)
        {
            _ignored++;
            return 0;
        }

        var local = epoch + IndiaDays.OffsetSeconds;
        var day = IndiaDays.FloorDiv(local, DaySeconds);
        var minute = (int)((local - day * DaySeconds) / 60);
        var midnight = day * DaySeconds - IndiaDays.OffsetSeconds;
        var bucketMinute = _timeframe == Timeframe.D1 ? 0 : SessionClock.OpenMinute + (int)IndiaDays.FloorDiv(minute - SessionClock.OpenMinute, _minutes) * _minutes;
        var start = midnight + bucketMinute * 60L;

        if (_emitted && start <= _lastEmittedStart)
        {
            _ignored++;
            return 0;
        }

        _seen = true;
        _lastSeen = epoch;

        var closed = 0;
        if (_active && start != _start)
        {
            first = Emit();
            closed = 1;
        }

        if (!_active)
        {
            Begin(start, bucketMinute, day, minute, minuteBar);
        }
        else
        {
            _high = Math.Max(_high, minuteBar.High);
            _low = Math.Min(_low, minuteBar.Low);
            _close = minuteBar.Close;
            _volume += minuteBar.Volume;
            _bars++;
            _lastEpoch = epoch;
            if (minute >= SessionClock.OpenMinute && minute < SessionClock.CloseMinute)
            {
                _regularBars++;
            }
        }

        if (minute + 1 >= _windowEnd)
        {
            var own = Emit();
            if (closed == 0)
            {
                first = own;
            }
            else
            {
                second = own;
            }

            closed++;
        }

        return closed;
    }

    /// <summary>Forgets every bar fed so far.</summary>
    public void Reset()
    {
        _periods?.Reset();
        _active = false;
        _seen = false;
        _emitted = false;
        _lastSeen = 0;
        _lastEmittedStart = 0;
        _ignored = 0;
    }

    private void Begin(long start, int bucketMinute, long day, int minute, in Bar bar)
    {
        _active = true;
        _start = start;
        _open = bar.Open;
        _high = bar.High;
        _low = bar.Low;
        _close = bar.Close;
        _volume = bar.Volume;
        _bars = 1;
        _regularBars = minute >= SessionClock.OpenMinute && minute < SessionClock.CloseMinute ? 1 : 0;
        _firstEpoch = bar.EpochSeconds;
        _lastEpoch = bar.EpochSeconds;
        _windowEnd = WindowEnd(bucketMinute, day, minute);
    }

    /// <summary>The first minute of the day that is no longer part of the bucket: the bar at the minute before it closes the bucket.</summary>
    private int WindowEnd(int bucketMinute, long day, int firstMinute)
    {
        var sessionEnd = int.MaxValue;

        // A date outside the range of DateOnly (a garbage epoch) is in no calendar: its day has no special session.
        if (_calendar is not null && IndiaDays.TryDateOf(day, out var date) && _calendar.SessionOf(date) is { } session && session.ToMinute > firstMinute)
        {
            sessionEnd = session.ToMinute;
        }

        if (_timeframe == Timeframe.D1)
        {
            // Without a calendar entry a day that starts in the regular session ends at the 15:30 close; an evening session ends when a later date begins.
            return sessionEnd != int.MaxValue ? sessionEnd : firstMinute < SessionClock.CloseMinute ? SessionClock.CloseMinute : int.MaxValue;
        }

        var end = bucketMinute + _minutes;
        if (bucketMinute < SessionClock.CloseMinute)
        {
            end = Math.Min(end, SessionClock.CloseMinute);
        }

        return Math.Min(end, sessionEnd);
    }

    private ClosedBar Emit()
    {
        _active = false;
        _emitted = true;
        _lastEmittedStart = _start;

        bool complete;
        if (_timeframe == Timeframe.D1)
        {
            var span = (_lastEpoch - _firstEpoch) / 60 + 1;
            complete = _regularBars == SessionClock.RegularBars || (_regularBars == 0 && _bars == span);
        }
        else
        {
            var bucketMinute = (int)IndiaDays.FloorMod(_start + IndiaDays.OffsetSeconds, DaySeconds) / 60;
            var expected = bucketMinute < SessionClock.CloseMinute ? Math.Min(_minutes, SessionClock.CloseMinute - bucketMinute) : _minutes;
            complete = _bars == expected;
        }

        return new ClosedBar(_start, new Bar(_start, _open, _high, _low, _close, _volume), _bars, complete, _timeframe);
    }
}
