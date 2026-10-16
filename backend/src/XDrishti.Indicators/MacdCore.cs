namespace XDrishti.Indicators;

/// <summary>
/// The shared state of <see cref="MacdLine"/>, <see cref="MacdSignal"/> and <see cref="MacdHist"/>: <c>line = ema(fast) − ema(slow)</c> of the source (both seeded with the simple mean of their first values, so the
/// line is ready at bar <c>slow</c>); <c>signal = ema(g)</c> of the line, seeded with the simple mean of the line's first <c>g</c> values (ready at bar <c>slow + g − 1</c>); <c>hist = line − signal</c>.
/// </summary>
internal struct MacdCore
{
    private EmaCore _fast;

    private EmaCore _slow;

    private EmaCore _signal;

    public MacdCore(int fast, int slow, int signal)
    {
        _fast = new EmaCore(fast);
        _slow = new EmaCore(slow);
        _signal = new EmaCore(signal);
        Line = double.NaN;
    }

    public double Line { get; private set; }

    public readonly double Signal => _signal.Value;

    public readonly double Histogram => _signal.IsReady ? Line - _signal.Value : double.NaN;

    public void Add(double x)
    {
        _fast.Add(x);
        _slow.Add(x);
        if (_slow.IsReady)
        {
            Line = _fast.Value - _slow.Value;
            _signal.Add(Line);
        }
    }

    public void Clear()
    {
        _fast.Clear();
        _slow.Clear();
        _signal.Clear();
        Line = double.NaN;
    }
}
