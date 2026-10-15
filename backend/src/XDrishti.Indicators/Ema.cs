using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>ema(n)</c>: <c>α = 2/(n+1)</c>, seeded with the simple mean of the first <c>n</c> values (ready at bar <c>n</c>), then <c>ema = α·x + (1−α)·ema</c>. The same seeding as Skender.Stock.Indicators.
/// </summary>
public sealed class Ema : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private readonly double _alpha;

    private readonly double _keep;

    private CompensatedSum _seed;

    private int _count;

    private double _value = double.NaN;

    public Ema(int period, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
        _alpha = 2.0 / (period + 1.0);
        _keep = 1.0 - _alpha;
    }

    public double Value => _value;

    public bool IsReady => _count >= _period;

    public int WarmUp => _period;

    public void Update(in Bar bar)
    {
        var x = PriceSources.Pick(_source, bar);
        if (_count < _period)
        {
            _seed.Add(x);
            _count++;
            if (_count == _period)
            {
                _value = _seed.Value / _period;
            }

            return;
        }

        _value = _alpha * x + _keep * _value;
    }

    public void Reset()
    {
        _seed.Clear();
        _count = 0;
        _value = double.NaN;
    }
}
