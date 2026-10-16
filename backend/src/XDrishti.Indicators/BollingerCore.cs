namespace XDrishti.Indicators;

/// <summary>
/// The shared state of the Bollinger band features: a ring of the last <c>n</c> values, their mean (a running sum with Neumaier compensation, as <see cref="Sma"/>) and, when asked for, the <b>population</b>
/// standard deviation (÷ n, as TradingView and Skender) as a second pass over the window. The second pass is O(n) per update but exact; the O(1) identity Σx² − n·mean² cancels catastrophically when the window is
/// tight against a large price, and a flat window must give exactly 0.
/// </summary>
internal struct BollingerCore
{
    private readonly int _period;

    private readonly bool _needSigma;

    private readonly double[] _window;

    private CompensatedSum _sum;

    private int _next;

    private int _count;

    public BollingerCore(int period, bool needSigma)
    {
        _period = period;
        _needSigma = needSigma;
        _window = new double[period];
        _sum = default;
        _next = 0;
        _count = 0;
        Middle = double.NaN;
        Sigma = double.NaN;
    }

    public double Middle { get; private set; }

    public double Sigma { get; private set; }

    public void Add(double x)
    {
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
        if (_count < _period)
        {
            return;
        }

        var mean = _sum.Value / _period;
        Middle = mean;
        if (!_needSigma)
        {
            return;
        }

        var squares = 0.0;
        var window = _window;
        for (var i = 0; i < window.Length; i++)
        {
            var d = window[i] - mean;
            squares += d * d;
        }

        Sigma = Math.Sqrt(squares / _period);
    }

    public void Clear()
    {
        _sum.Clear();
        _next = 0;
        _count = 0;
        Middle = double.NaN;
        Sigma = double.NaN;
        Array.Clear(_window);
    }
}
