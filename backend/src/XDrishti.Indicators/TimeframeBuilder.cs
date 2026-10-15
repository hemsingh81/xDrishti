using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators;

/// <summary>
/// Builds <b>closed</b> 5m, 10m, 15m, 30m, 1h or 1D bars from the 1-minute stream with exactly the rules of the stored aggregates (design doc 05 §4, <c>BarAggregates</c>): intraday buckets are anchored to the
/// 09:15 India open, the last buckets are clipped at the 15:30 close (the 1h bucket at 15:15 holds 15 bars, the 10m bucket at 15:25 five), the day is the India date; open = first, high = max, low = min, close = last,
/// volume = sum, <c>Bars</c> the number of 1-minute bars used, <c>Complete</c> as stored (an intraday bucket holds exactly the minutes of its window; a day has all 375 regular minutes, or, with none of them, every
/// minute between its first and last bar).
/// <para>
/// A bucket is emitted when the bar that ends its window has been fed (the 15:29 bar for a bucket clipped at the close and for a regular day), or, if that minute never arrives, by the first bar of a later bucket —
/// flagged <c>Complete = false</c>. Given a <see cref="TradingCalendar"/>, the last minute of a special session also ends its buckets and its day. A bucket is emitted exactly once and never while it is forming.
/// Bars must come in time order: a bar at or before the previous one, or for a bucket already emitted, is ignored and counted in <see cref="Ignored"/>. Thread-confined; allocates nothing after construction.
/// </para>
/// </summary>
public sealed class TimeframeBuilder
{
    private const long DaySeconds = IndiaDays.SecondsPerDay;

    /// <summary>Days from 0001-01-01 (<c>DateOnly.DayNumber</c>) to 1970-01-01.</summary>
    private const int DateOnlyEpochDayNumber = 719_162;

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

    /// <param name="timeframe">5m, 10m, 15m, 30m, 1h or 1D.</param>
    /// <param name="calendar">Optional: lets a special session close its last bucket and its day at its own last minute instead of waiting for the next bucket. Without it only the regular close is known.</param>
    public TimeframeBuilder(Timeframe timeframe, TradingCalendar? calendar = null)
    {
        if (timeframe is not (Timeframe.M5 or Timeframe.M10 or Timeframe.M15 or Timeframe.M30 or Timeframe.H1 or Timeframe.D1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Only 5m, 10m, 15m, 30m, 1h and 1D are built.");
        }

        _timeframe = timeframe;
        _minutes = timeframe.Minutes();
        _calendar = calendar;
    }

    public Timeframe Timeframe => _timeframe;

    /// <summary>How many bars were ignored because they were out of order or belonged to a bucket that had been emitted already (0 on valid data).</summary>
    public long Ignored { get; private set; }

    /// <summary>
    /// Feeds the next 1-minute bar and returns how many buckets it closed (0, 1 or 2): a bar can close the previous bucket (a later bucket began) and, if it is the last minute of its own window and the first of its
    /// bucket, close that one too. The first closed bucket is in <paramref name="first"/>, the second in <paramref name="second"/>.
    /// </summary>
    public int Push(in Bar minuteBar, out ClosedBar first, out ClosedBar second)
    {
        first = default;
        second = default;
        var epoch = minuteBar.EpochSeconds;
        if (_seen && epoch <= _lastSeen)
        {
            Ignored++;
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
            Ignored++;
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
        _active = false;
        _seen = false;
        _emitted = false;
        _lastSeen = 0;
        _lastEmittedStart = 0;
        Ignored = 0;
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
        if (_calendar?.SessionOf(DateOnly.FromDayNumber((int)day + DateOnlyEpochDayNumber)) is { } session && session.ToMinute > firstMinute)
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
