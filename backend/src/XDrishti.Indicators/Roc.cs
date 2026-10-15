using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>roc(n)</c>: the rate of change in percent, <c>100·(x − x[n])/x[n]</c> where <c>x[n]</c> is the value of the source <c>n</c> bars ago; ready at bar <c>n+1</c>. Not ready (NaN) while that base is 0.
/// </summary>
public sealed class Roc : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private readonly double[] _window;

    private int _next;

    private int _count;

    private double _value = double.NaN;

    public Roc(int period, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
        _window = new double[period];
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _period + 1;

    public void Update(in Bar bar)
    {
        var x = PriceSources.Pick(_source, bar);
        if (_count >= _period)
        {
            // the slot about to be overwritten holds the value from n bars ago
            var origin = _window[_next];
            _value = origin == 0 ? double.NaN : 100.0 * (x - origin) / origin;
        }
        else
        {
            _count++;
        }

        _window[_next] = x;
        _next = _next + 1 == _period ? 0 : _next + 1;
    }

    public void Reset()
    {
        Array.Clear(_window);
        _next = 0;
        _count = 0;
        _value = double.NaN;
    }
}
