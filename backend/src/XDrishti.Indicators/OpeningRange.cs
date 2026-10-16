using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The shared state of <see cref="OrHigh"/> and <see cref="OrLow"/>: the extremes of the bars that start in the first <c>m</c> minutes of the session (09:15 up to, not including, 09:15 + m). The range is
/// ready once a bar that reaches the last minute of the window (start + bar length − 1 ≥ 09:15 + m − 1) has been fed, or a bar after the window has; it stays constant until the date changes.
/// </summary>
internal struct OpeningRange
{
    private readonly int _from;

    private readonly int _to;

    private readonly int _barMinutes;

    private double _high;

    private double _low;

    private long _day;

    private bool _started;

    private bool _any;

    private bool _ready;

    public OpeningRange(int minutes, int barMinutes)
    {
        _from = SessionClock.OpenMinute;
        _to = SessionClock.OpenMinute + minutes;
        _barMinutes = barMinutes;
        _high = double.NaN;
        _low = double.NaN;
        _day = 0;
        _started = false;
        _any = false;
        _ready = false;
    }

    public readonly bool IsReady => _ready;

    public readonly double High => _ready ? _high : double.NaN;

    public readonly double Low => _ready ? _low : double.NaN;

    public void Update(in Bar bar)
    {
        var day = IndiaDays.DayOf(bar.EpochSeconds);
        if (!_started || day != _day)
        {
            _day = day;
            _started = true;
            _any = false;
            _ready = false;
            _high = double.NaN;
            _low = double.NaN;
        }

        if (_ready)
        {
            return;
        }

        var start = IndiaDays.MinuteOfDay(bar.EpochSeconds);
        if (start >= _from && start < _to)
        {
            if (_any)
            {
                _high = Math.Max(_high, bar.High);
                _low = Math.Min(_low, bar.Low);
            }
            else
            {
                _high = bar.High;
                _low = bar.Low;
                _any = true;
            }

            _ready = start + _barMinutes >= _to;
        }
        else if (start >= _to)
        {
            _ready = _any;
        }
    }

    public void Clear()
    {
        _day = 0;
        _started = false;
        _any = false;
        _ready = false;
        _high = double.NaN;
        _low = double.NaN;
    }
}
