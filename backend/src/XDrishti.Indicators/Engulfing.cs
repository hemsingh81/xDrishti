using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>engulfing</c>: +1 for a bullish and −1 for a bearish engulfing candle, otherwise 0, judged on the bodies (open and close) and not on the full high–low range. Both bodies must be above 0. Bullish: the previous bar
/// is down (close below open), the current is up and <c>open ≤ previous close</c> and <c>close ≥ previous open</c>; bearish is the mirror image. Ready at bar 2.
/// </summary>
public sealed class Engulfing : IIndicator
{
    private double _previousOpen;

    private double _previousClose;

    private bool _seen;

    private double _value = double.NaN;

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => 2;

    public void Update(in Bar bar)
    {
        if (_seen)
        {
            var bullish = _previousClose < _previousOpen && bar.Close > bar.Open && bar.Open <= _previousClose && bar.Close >= _previousOpen;
            var bearish = _previousClose > _previousOpen && bar.Close < bar.Open && bar.Open >= _previousClose && bar.Close <= _previousOpen;
            _value = bullish ? 1 : bearish ? -1 : 0;
        }

        _seen = true;
        _previousOpen = bar.Open;
        _previousClose = bar.Close;
    }

    public void Reset()
    {
        _seen = false;
        _previousOpen = 0;
        _previousClose = 0;
        _value = double.NaN;
    }
}
