namespace XDrishti.Indicators;

/// <summary>
/// The exponential moving average on plain numbers, shared by <see cref="Ema"/> and the MACD family: <c>α = 2/(n+1)</c>, seeded with the simple mean of the first <c>n</c> values (ready after the n-th),
/// then <c>ema = α·x + (1−α)·ema</c>.
/// </summary>
internal struct EmaCore
{
    private readonly int _period;

    private readonly double _alpha;

    private readonly double _keep;

    private CompensatedSum _seed;

    private int _count;

    private double _value;

    public EmaCore(int period)
    {
        _period = period;
        _alpha = 2.0 / (period + 1.0);
        _keep = 1.0 - _alpha;
        _seed = default;
        _count = 0;
        _value = double.NaN;
    }

    public readonly bool IsReady => _count >= _period;

    public readonly double Value => _value;

    public void Add(double x)
    {
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

    public void Clear()
    {
        _seed.Clear();
        _count = 0;
        _value = double.NaN;
    }
}
