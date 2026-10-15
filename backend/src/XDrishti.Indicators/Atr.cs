using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// <c>atr(n)</c> (Wilder): the true range is <c>high − low</c> for the first bar and <c>max(high − low, |high − previous close|, |low − previous close|)</c> after it. The average is seeded with
/// the mean of the first <c>n</c> true ranges (ready at bar <c>n</c>), then <c>atr = (atr·(n−1) + tr)/n</c>. Skender.Stock.Indicators seeds one bar later (ADR 0031).
/// </summary>
public sealed class Atr : IIndicator
{
    private readonly int _period;

    private CompensatedSum _seed;

    private double _previousClose;

    private bool _seenFirst;

    private int _count;

    private double _value = double.NaN;

    public Atr(int period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        _period = period;
    }

    public double Value => _value;

    public bool IsReady => _count >= _period;

    public int WarmUp => _period;

    public void Update(in Bar bar)
    {
        var range = bar.High - bar.Low;
        if (_seenFirst)
        {
            range = Math.Max(range, Math.Max(Math.Abs(bar.High - _previousClose), Math.Abs(bar.Low - _previousClose)));
        }

        _seenFirst = true;
        _previousClose = bar.Close;

        if (_count < _period)
        {
            _seed.Add(range);
            _count++;
            if (_count == _period)
            {
                _value = _seed.Value / _period;
            }

            return;
        }

        _value = (_value * (_period - 1) + range) / _period;
    }

    public void Reset()
    {
        _seed.Clear();
        _previousClose = 0;
        _seenFirst = false;
        _count = 0;
        _value = double.NaN;
    }
}
