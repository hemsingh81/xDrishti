using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The shared state of the pivot features (<see cref="PivotHigh"/>, <see cref="PivotLow"/> and their age companions): the most recent <b>confirmed</b> swing point. A bar is a pivot high when its high is strictly
/// above the highs of the <c>n</c> bars before it <b>and</b> the <c>n</c> bars after it (a pivot low: its low strictly below the lows); equal values are not pivots. It is confirmed only when the n-th bar after
/// it has closed — at that moment it is examined for the first time — so the value never depends on a bar after the one just fed and arrives <c>n</c> bars late. <see cref="Value"/> is the price of the latest
/// pivot, <see cref="Age"/> the number of bars since it (n at the bar that confirmed it, then growing by one per bar); both are NaN until the first pivot is confirmed.
/// </summary>
internal struct PivotCore
{
    private readonly int _period;

    private readonly bool _high;

    private readonly double[] _window;

    private int _next;

    private int _count;

    private long _fed;

    private long _pivotBar;

    public PivotCore(int period, bool high)
    {
        _period = period;
        _high = high;
        _window = new double[2 * period + 1];
        _next = 0;
        _count = 0;
        _fed = 0;
        _pivotBar = 0;
        Value = double.NaN;
        Age = double.NaN;
    }

    public double Value { get; private set; }

    public double Age { get; private set; }

    public void Update(in Bar bar)
    {
        var window = _window;
        var size = window.Length;
        window[_next] = _high ? bar.High : bar.Low;
        _next = _next + 1 == size ? 0 : _next + 1;
        _fed++;
        if (_count < size)
        {
            _count++;
        }

        if (_count == size)
        {
            // the oldest slot is the first bar of the window; the candidate sits n bars after it, i.e. n bars before the bar just fed
            var candidateSlot = _next + _period;
            if (candidateSlot >= size)
            {
                candidateSlot -= size;
            }

            var candidate = window[candidateSlot];
            var isPivot = true;
            for (var i = 0; i < size && isPivot; i++)
            {
                if (i != candidateSlot)
                {
                    isPivot = _high ? candidate > window[i] : candidate < window[i];
                }
            }

            if (isPivot)
            {
                Value = candidate;
                _pivotBar = _fed - _period;
            }
        }

        if (!double.IsNaN(Value))
        {
            Age = _fed - _pivotBar;
        }
    }

    public void Clear()
    {
        Array.Clear(_window);
        _next = 0;
        _count = 0;
        _fed = 0;
        _pivotBar = 0;
        Value = double.NaN;
        Age = double.NaN;
    }
}
