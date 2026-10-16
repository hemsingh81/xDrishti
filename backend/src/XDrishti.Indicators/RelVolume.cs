using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>rel_volume(n)</c>: the bar's volume divided by the mean volume of the <b>previous</b> <c>n</c> bars (the current bar is not in the denominator). Ready at bar <c>n+1</c>; not ready (NaN)
/// while that mean is 0. Volumes are whole numbers, so the running sum is exact.
/// </summary>
public sealed class RelVolume : IIndicator
{
    private readonly int _period;

    private readonly long[] _window;

    private long _sum;

    private int _next;

    private int _count;

    private double _value = double.NaN;

    public RelVolume(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _window = new long[period];
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _period + 1;

    public void Update(in Bar bar)
    {
        if (_count >= _period)
        {
            _value = _sum > 0 ? bar.Volume * (double)_period / _sum : double.NaN;
            _sum -= _window[_next];
        }
        else
        {
            _count++;
        }

        _window[_next] = bar.Volume;
        _sum += bar.Volume;
        _next = _next + 1 == _period ? 0 : _next + 1;
    }

    public void Reset()
    {
        Array.Clear(_window);
        _sum = 0;
        _next = 0;
        _count = 0;
        _value = double.NaN;
    }
}
