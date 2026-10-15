using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>nr(n)</c>: 1 when the bar's range (high − low) is strictly smaller than the range of each of the previous <c>n − 1</c> bars (so ties are not narrower), otherwise 0; <c>n</c> is at least 2. Ready at bar <c>n</c>.
/// </summary>
public sealed class NarrowRange : IIndicator
{
    private readonly int _period;

    private MonotoneWindow _previous;

    private double _value = double.NaN;

    public NarrowRange(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 2);
        _period = period;
        _previous = new MonotoneWindow(period - 1, maximum: false);
    }

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _period;

    public void Update(in Bar bar)
    {
        var range = bar.High - bar.Low;
        if (_previous.Fed >= _period - 1)
        {
            _value = range < _previous.Extreme ? 1 : 0;
        }

        _previous.Add(range);
    }

    public void Reset()
    {
        _previous.Clear();
        _value = double.NaN;
    }
}
