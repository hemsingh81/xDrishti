using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>rsi(n)</c> (Wilder): the average gain and loss are seeded with the simple mean of the first <c>n</c> changes of the source, then <c>avg = (avg·(n−1) + x)/n</c>; <c>rsi = 100 − 100/(1+RS)</c>.
/// 100 when the average loss is 0 and the average gain is above 0, 50 when both are 0. Ready at bar <c>n+1</c> (the first change needs two bars).
/// </summary>
public sealed class Rsi : IIndicator
{
    private readonly int _period;

    private readonly PriceSource _source;

    private CompensatedSum _gainSeed;

    private CompensatedSum _lossSeed;

    private double _gain;

    private double _loss;

    private double _previous;

    private bool _seenFirst;

    private int _changes;

    private double _value = double.NaN;

    public Rsi(int period, PriceSource source = PriceSource.Close)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
        _source = source;
    }

    public double Value => _value;

    public bool IsReady => _changes >= _period;

    public int WarmUp => _period + 1;

    public void Update(in Bar bar)
    {
        var x = PriceSources.Pick(_source, bar);
        if (!_seenFirst)
        {
            _seenFirst = true;
            _previous = x;
            return;
        }

        var change = x - _previous;
        _previous = x;
        var gain = change > 0 ? change : 0;
        var loss = change < 0 ? -change : 0;

        if (_changes < _period)
        {
            _gainSeed.Add(gain);
            _lossSeed.Add(loss);
            _changes++;
            if (_changes < _period)
            {
                return;
            }

            _gain = _gainSeed.Value / _period;
            _loss = _lossSeed.Value / _period;
        }
        else
        {
            _gain = (_gain * (_period - 1) + gain) / _period;
            _loss = (_loss * (_period - 1) + loss) / _period;
        }

        _value = Level(_gain, _loss);
    }

    public void Reset()
    {
        _gainSeed.Clear();
        _lossSeed.Clear();
        _gain = 0;
        _loss = 0;
        _previous = 0;
        _seenFirst = false;
        _changes = 0;
        _value = double.NaN;
    }

    private static double Level(double gain, double loss)
    {
        if (loss == 0)
        {
            return gain > 0 ? 100 : 50;
        }

        return 100 - 100 / (1 + gain / loss);
    }

}
