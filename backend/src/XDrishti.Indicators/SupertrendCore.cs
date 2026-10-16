using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The shared state of <see cref="Supertrend"/> and <see cref="SupertrendDirection"/> (TradingView-style, ADR 0032). <c>hl2 = (high + low)/2</c>; the basic bands are <c>hl2 ± m·atr</c> with the Wilder
/// <see cref="Atr"/> of <c>n</c> bars. The final upper band is the basic upper band when that is below the previous final upper band or the previous close was above it, otherwise the previous final upper band
/// (mirrored for the lower band). The direction is decided after the bands of the bar are final: it turns up when the close is above the <i>current</i> final upper band while it is down, and down when the close is
/// below the <i>current</i> final lower band while it is up (as TradingView and Skender.Stock.Indicators do). The value is the final lower band while the direction is up and the final upper band while it is down. Everything starts at the bar the ATR is ready (bar <c>n</c>), with direction up.
/// </summary>
internal struct SupertrendCore
{
    private readonly double _multiplier;

    private readonly Atr _atr;

    private double _upper;

    private double _lower;

    private double _previousClose;

    private bool _started;

    private bool _up;

    public SupertrendCore(int period, double multiplier)
    {
        _multiplier = multiplier;
        _atr = new Atr(period);
        _upper = 0;
        _lower = 0;
        _previousClose = 0;
        _started = false;
        _up = true;
        Value = double.NaN;
        Direction = double.NaN;
    }

    public double Value { get; private set; }

    /// <summary>+1 while the trend is up, −1 while it is down, NaN before the first value.</summary>
    public double Direction { get; private set; }

    public void Update(in Bar bar)
    {
        _atr.Update(bar);
        if (!_atr.IsReady)
        {
            _previousClose = bar.Close;
            return;
        }

        var middle = (bar.High + bar.Low) / 2;
        var offset = _multiplier * _atr.Value;
        var basicUpper = middle + offset;
        var basicLower = middle - offset;

        if (!_started)
        {
            _started = true;
            _upper = basicUpper;
            _lower = basicLower;
            _up = true;
        }
        else
        {
            var previousUpper = _upper;
            var previousLower = _lower;
            _upper = basicUpper < previousUpper || _previousClose > previousUpper ? basicUpper : previousUpper;
            _lower = basicLower > previousLower || _previousClose < previousLower ? basicLower : previousLower;
            if (!_up && bar.Close > _upper)
            {
                _up = true;
            }
            else if (_up && bar.Close < _lower)
            {
                _up = false;
            }
        }

        _previousClose = bar.Close;
        Value = _up ? _lower : _upper;
        Direction = _up ? 1 : -1;
    }

    public void Clear()
    {
        _atr.Reset();
        _upper = 0;
        _lower = 0;
        _previousClose = 0;
        _started = false;
        _up = true;
        Value = double.NaN;
        Direction = double.NaN;
    }
}
