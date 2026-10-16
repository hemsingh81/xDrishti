namespace XDrishti.Indicators;

/// <summary>
/// A running sum with Neumaier compensation: the rounding error of every addition is collected in a second term, so a window that adds a value and later subtracts it again does not
/// drift (unlike a plain running sum). Deterministic: a fixed sequence of double operations.
/// </summary>
internal struct CompensatedSum
{
    private double _sum;

    private double _compensation;

    public readonly double Value => _sum + _compensation;

    public void Add(double x)
    {
        var t = _sum + x;
        _compensation += Math.Abs(_sum) >= Math.Abs(x) ? (_sum - t) + x : (x - t) + _sum;
        _sum = t;
    }

    public void Clear()
    {
        _sum = 0;
        _compensation = 0;
    }
}
