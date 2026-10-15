using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The shared state of <see cref="PlusDi"/>, <see cref="MinusDi"/> and <see cref="Adx"/> (Wilder's directional movement system). From the second bar on: up = high − previous high, down = previous low − low,
/// +DM = up when up &gt; down and up &gt; 0, −DM = down when down &gt; up and down &gt; 0, TR the true range. Each of the three is smoothed with Wilder's sum recursion (the seed is the sum of the first <c>n</c>
/// values, then <c>s = s − s/n + x</c>); +DI = 100·s(+DM)/s(TR), −DI likewise (0 when s(TR) is 0). DX = 100·|+DI − −DI|/(+DI + −DI) (0 when both are 0). ADX is seeded with the mean of the first <c>n</c> DX
/// values, then <c>adx = (adx·(n−1) + dx)/n</c>. The DIs are ready at bar <c>n+1</c>, the ADX at bar <c>2n</c>.
/// </summary>
internal struct DirectionalMovement
{
    private readonly int _period;

    private CompensatedSum _trSeed;

    private CompensatedSum _plusSeed;

    private CompensatedSum _minusSeed;

    private CompensatedSum _dxSeed;

    private double _high;

    private double _low;

    private double _close;

    private bool _seenFirst;

    private int _moves;

    private int _dxCount;

    private double _trSum;

    private double _plusSum;

    private double _minusSum;

    public DirectionalMovement(int period)
    {
        _period = period;
        _trSeed = default;
        _plusSeed = default;
        _minusSeed = default;
        _dxSeed = default;
        _high = 0;
        _low = 0;
        _close = 0;
        _seenFirst = false;
        _moves = 0;
        _dxCount = 0;
        _trSum = 0;
        _plusSum = 0;
        _minusSum = 0;
        PlusDi = double.NaN;
        MinusDi = double.NaN;
        Adx = double.NaN;
    }

    public double PlusDi { get; private set; }

    public double MinusDi { get; private set; }

    public double Adx { get; private set; }

    public void Update(in Bar bar)
    {
        if (!_seenFirst)
        {
            _seenFirst = true;
            Remember(bar);
            return;
        }

        var up = bar.High - _high;
        var down = _low - bar.Low;
        var plus = up > down && up > 0 ? up : 0;
        var minus = down > up && down > 0 ? down : 0;
        var range = Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - _close), Math.Abs(bar.Low - _close)));
        Remember(bar);

        if (_moves < _period)
        {
            _trSeed.Add(range);
            _plusSeed.Add(plus);
            _minusSeed.Add(minus);
            _moves++;
            if (_moves < _period)
            {
                return;
            }

            _trSum = _trSeed.Value;
            _plusSum = _plusSeed.Value;
            _minusSum = _minusSeed.Value;
        }
        else
        {
            _trSum = _trSum - _trSum / _period + range;
            _plusSum = _plusSum - _plusSum / _period + plus;
            _minusSum = _minusSum - _minusSum / _period + minus;
        }

        var plusDi = _trSum > 0 ? 100 * _plusSum / _trSum : 0;
        var minusDi = _trSum > 0 ? 100 * _minusSum / _trSum : 0;
        PlusDi = plusDi;
        MinusDi = minusDi;

        var total = plusDi + minusDi;
        var dx = total > 0 ? 100 * Math.Abs(plusDi - minusDi) / total : 0;
        if (_dxCount < _period)
        {
            _dxSeed.Add(dx);
            _dxCount++;
            if (_dxCount == _period)
            {
                Adx = _dxSeed.Value / _period;
            }
        }
        else
        {
            Adx = (Adx * (_period - 1) + dx) / _period;
        }
    }

    public void Clear()
    {
        _trSeed.Clear();
        _plusSeed.Clear();
        _minusSeed.Clear();
        _dxSeed.Clear();
        _high = 0;
        _low = 0;
        _close = 0;
        _seenFirst = false;
        _moves = 0;
        _dxCount = 0;
        _trSum = 0;
        _plusSum = 0;
        _minusSum = 0;
        PlusDi = double.NaN;
        MinusDi = double.NaN;
        Adx = double.NaN;
    }

    private void Remember(in Bar bar)
    {
        _high = bar.High;
        _low = bar.Low;
        _close = bar.Close;
    }
}
