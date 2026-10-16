using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>sma(n)</c>: the mean of the last <c>n</c> values of the source (default close); ready at bar <c>n</c>. A running sum with Neumaier compensation over a ring buffer, so it does not drift and agrees
/// with the batch mean within 1e-9 relative.
/// </summary>
public sealed class Sma : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private readonly double[] _window;

    private CompensatedSum _sum;

    private int _next;

    private int _count;

    private double _value = double.NaN;

    public Sma(int period, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
        _window = new double[period];
    }

    public double Value => _value;

    public bool IsReady => _count >= _period;

    public int WarmUp => _period;

    public void Update(in Bar bar)
    {
        var x = PriceSources.Pick(_source, bar);
        if (_count >= _period)
        {
            _sum.Add(-_window[_next]);
        }
        else
        {
            _count++;
        }

        _window[_next] = x;
        _sum.Add(x);
        _next = _next + 1 == _period ? 0 : _next + 1;
        if (_count >= _period)
        {
            _value = _sum.Value / _period;
        }
    }

    public void Reset()
    {
        _sum.Clear();
        _next = 0;
        _count = 0;
        _value = double.NaN;
        Array.Clear(_window);
    }
}
