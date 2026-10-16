namespace XDrishti.Indicators;

/// <summary>
/// The percentile rank of the newest value among the last <c>size</c> values (the market context, ADR 0033): <c>100 × (#below + 0.5 × #equal) / size</c>, where #equal counts the newest value itself, so ties
/// take the middle of their span (a window of identical values ranks 50, the lowest value of a window of 4 ranks 12.5, the highest 87.5). Not defined (NaN) until <c>size</c> values have been pushed. The counts
/// are integers compared with exact equality, so the rank is a pure function of the window's contents; the work per value is one pass over the window (a few hundred comparisons for the usual 252).
/// <para>
/// The window also hands out the rank as a whole number, <b>twice the rank</b> (<c>2 × #below + #equal</c>, <see cref="PushDoubleRank"/>): the rank in percent is <c>100 × that / (2 × size)</c>, and a score built from
/// ranks (the mean of two of them) stays an exact fraction of integers until one last division, which is correctly rounded. That is what lets "exactly one third" be recognised as the double <c>100.0 / 3</c>.
/// </para>
/// </summary>
internal sealed class PercentileWindow
{
    /// <summary>The double rank of a value that has no rank yet (fewer than <c>size</c> values pushed) or none at all (the day has no value).</summary>
    public const int NotRanked = -1;

    private readonly double[] _values;

    private int _next;

    private int _count;

    public PercentileWindow(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        _values = new double[size];
    }

    /// <summary>The rank in percent of a double rank (<c>2 × #below + #equal</c>) among <paramref name="size"/> values: one division of exact integers, so correctly rounded; NaN for <see cref="NotRanked"/>.</summary>
    public static double Percent(int doubleRank, int size) => doubleRank < 0 ? double.NaN : 100.0 * doubleRank / (2.0 * size);

    /// <summary>Adds a value (dropping the oldest one when the window is full) and returns its rank, or NaN while fewer than <c>size</c> values have been pushed.</summary>
    public double Push(double value) => Percent(PushDoubleRank(value), _values.Length);

    /// <summary>Adds a value (dropping the oldest one when the window is full) and returns twice its rank, <c>2 × #below + #equal</c>, or <see cref="NotRanked"/> while fewer than <c>size</c> values have been pushed.</summary>
    public int PushDoubleRank(double value)
    {
        _values[_next] = value;
        _next = _next + 1 == _values.Length ? 0 : _next + 1;
        if (_count < _values.Length)
        {
            _count++;
            if (_count < _values.Length)
            {
                return NotRanked;
            }
        }

        var below = 0;
        var equal = 0;
        foreach (var other in _values)
        {
            if (other < value)
            {
                below++;
            }
            else if (other == value)
            {
                equal++;
            }
        }

        return 2 * below + equal;
    }
}
