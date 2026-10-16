namespace XDrishti.Indicators;

/// <summary>
/// The extreme of the last <c>n</c> values in amortised O(1): a monotone deque kept in two fixed ring buffers (value and bar index), so nothing is allocated after construction. With
/// <c>sign</c> +1 it tracks the maximum, with -1 the minimum (values are compared negated, which is exact for doubles).
/// </summary>
internal struct MonotoneWindow
{
    private readonly double[] _values;

    private readonly long[] _indexes;

    private readonly int _size;

    private readonly double _sign;

    private int _head;

    private int _count;

    private long _next;

    public MonotoneWindow(int size, bool maximum)
    {
        _size = size;
        _sign = maximum ? 1 : -1;
        _values = new double[size];
        _indexes = new long[size];
        _head = 0;
        _count = 0;
        _next = 0;
    }

    /// <summary>The number of values fed so far.</summary>
    public readonly long Fed => _next;

    /// <summary>The extreme of the last <c>n</c> values (valid once at least one value has been added).</summary>
    public readonly double Extreme => _sign * _values[_head];

    public void Add(double x)
    {
        var signed = _sign * x;
        var index = _next++;

        // Drop the values that fell out of the window (at most one per step).
        if (_count > 0 && _indexes[_head] <= index - _size)
        {
            _head = _head + 1 == _size ? 0 : _head + 1;
            _count--;
        }

        // Drop the values at the back that can never be the extreme again.
        while (_count > 0)
        {
            var back = _head + _count - 1;
            if (back >= _size)
            {
                back -= _size;
            }

            if (_values[back] > signed)
            {
                break;
            }

            _count--;
        }

        var slot = _head + _count;
        if (slot >= _size)
        {
            slot -= _size;
        }

        _values[slot] = signed;
        _indexes[slot] = index;
        _count++;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        _next = 0;
    }
}
