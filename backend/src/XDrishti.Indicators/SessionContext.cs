using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// One value of the session context: <c>prev_day</c> open/high/low/close/volume (the previous session <b>that had bars</b>, not "yesterday": weekends and holidays are skipped by the data, not guessed),
/// <c>today</c> open/high/low so far, and <c>gap_pct = (today.open − prev_day.close)/prev_day.close·100</c>. It resets per India trading date: the first bar of a new date makes the finished date the
/// previous session. <c>today.*</c> are ready from the first bar, <c>prev_day.*</c> and the gap from the first bar of the second session.
/// </summary>
public sealed class SessionContext : IIndicator
{
    private readonly SessionField _field;

    private readonly int _warmUp;

    private long _day;

    private bool _started;

    private bool _hasPrevious;

    private double _todayOpen;

    private double _todayHigh;

    private double _todayLow;

    private double _todayClose;

    private double _todayVolume;

    private double _prevOpen;

    private double _prevHigh;

    private double _prevLow;

    private double _prevClose;

    private double _prevVolume;

    /// <param name="field">Which value this instance reports.</param>
    /// <param name="barsPerSession">How many bars of the fed series make up a full first session (375 one-minute bars; used only for <see cref="WarmUp"/> of the previous-session values).</param>
    public SessionContext(SessionField field, int barsPerSession = SessionClock.RegularBars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(barsPerSession, 1);
        _field = field;
        _warmUp = field is SessionField.TodayOpen or SessionField.TodayHigh or SessionField.TodayLow ? 1 : barsPerSession + 1;
    }

    public double Value => IsReady ? Read() : double.NaN;

    public bool IsReady =>
        _started && (_field is SessionField.TodayOpen or SessionField.TodayHigh or SessionField.TodayLow || (_hasPrevious && (_field != SessionField.GapPct || _prevClose != 0)));

    public int WarmUp => _warmUp;

    public void Update(in Bar bar)
    {
        var day = IndiaDays.DayOf(bar.EpochSeconds);
        if (!_started || day != _day)
        {
            if (_started)
            {
                _prevOpen = _todayOpen;
                _prevHigh = _todayHigh;
                _prevLow = _todayLow;
                _prevClose = _todayClose;
                _prevVolume = _todayVolume;
                _hasPrevious = true;
            }

            _day = day;
            _started = true;
            _todayOpen = bar.Open;
            _todayHigh = bar.High;
            _todayLow = bar.Low;
            _todayClose = bar.Close;
            _todayVolume = bar.Volume;
            return;
        }

        _todayHigh = Math.Max(_todayHigh, bar.High);
        _todayLow = Math.Min(_todayLow, bar.Low);
        _todayClose = bar.Close;
        _todayVolume += bar.Volume;
    }

    public void Reset()
    {
        _day = 0;
        _started = false;
        _hasPrevious = false;
        _todayOpen = _todayHigh = _todayLow = _todayClose = _todayVolume = 0;
        _prevOpen = _prevHigh = _prevLow = _prevClose = _prevVolume = 0;
    }

    private double Read() => _field switch
    {
        SessionField.PrevOpen => _prevOpen,
        SessionField.PrevHigh => _prevHigh,
        SessionField.PrevLow => _prevLow,
        SessionField.PrevClose => _prevClose,
        SessionField.PrevVolume => _prevVolume,
        SessionField.TodayOpen => _todayOpen,
        SessionField.TodayHigh => _todayHigh,
        SessionField.TodayLow => _todayLow,
        _ => _prevClose == 0 ? double.NaN : (_todayOpen - _prevClose) / _prevClose * 100.0,
    };
}
