namespace XDrishti.Simulation;

/// <summary>
/// The values a trailing exit reads, recorded once per instrument and side at each closed bar of the evaluation timeframe (P4-01, D1 to D3): for the <i>k</i>-th closed bar, the instant it closed (the end of the minute that closed it, the
/// same instant as <see cref="EntryCandidate.DecisionEpochSeconds"/> of a candidate decided on it), its close and the value of each feature some candidate exit reads, <b>as of that close</b> and nothing later. Rows ascend strictly in
/// <see cref="CloseEpoch"/>. A feature that is not ready is NaN, and a rule treats NaN as "no new level". A step that closed two bars gives one row, with the later bar's values: the earlier bar has no row, so a rule sees no level from it.
/// <para>
/// The look-ahead rule is one function: a rule at the minute that starts at <c>t</c> may use bar <i>k</i> only if <c>CloseEpoch(k) &lt;= t</c>, which is what <see cref="LastClosedAtOrBefore"/> answers. A strategy whose exits read no feature
/// gets <see cref="Empty"/>, a shared instance that is never written to.
/// </para>
/// </summary>
public sealed class ExitTape
{
    private readonly string[] _features;
    private readonly double[][] _values;
    private long[] _epochs = [];
    private double[] _closes = [];
    private int _count;

    /// <summary>A tape for these features, empty so far.</summary>
    /// <param name="features">The names of the features recorded, in the order of the values of a row.</param>
    public ExitTape(IReadOnlyList<string> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _features = [.. features];
        _values = new double[_features.Length][];
        for (var f = 0; f < _values.Length; f++)
        {
            _values[f] = [];
        }
    }

    /// <summary>The tape of a strategy whose exits read nothing: shared, with no rows and no feature.</summary>
    public static ExitTape Empty { get; } = new([]);

    /// <summary>The names of the features, in the order of <see cref="Value"/>'s first argument.</summary>
    public IReadOnlyList<string> Features => _features;

    /// <summary>The rows so far.</summary>
    public int Count => _count;

    /// <summary>The instant bar <paramref name="bar"/> closed.</summary>
    public long CloseEpoch(int bar) => _epochs[CheckedBar(bar)];

    /// <summary>The close of bar <paramref name="bar"/>.</summary>
    public double Close(int bar) => _closes[CheckedBar(bar)];

    /// <summary>The value of feature number <paramref name="feature"/> as of the close of bar <paramref name="bar"/>; NaN while the feature is not ready.</summary>
    public double Value(int feature, int bar) => _values[feature][CheckedBar(bar)];

    /// <summary>The place of a feature in a row, or -1.</summary>
    public int IndexOf(string feature) => Array.IndexOf(_features, feature);

    /// <summary>The index of the latest bar whose close is at or before <paramref name="instant"/>, or -1 when none is. O(log n).</summary>
    public int LastClosedAtOrBefore(long instant)
    {
        var low = 0;
        var high = _count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (_epochs[middle] <= instant)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>Appends the row of a bar that closed at <paramref name="closeEpoch"/>, later than every row so far.</summary>
    internal void Append(long closeEpoch, double close, ReadOnlySpan<double> values)
    {
        if (ReferenceEquals(this, Empty) || values.Length != _features.Length || (_count > 0 && closeEpoch <= _epochs[_count - 1]))
        {
            throw new InvalidOperationException("The row does not fit the tape: a shared empty tape, a different number of values, or a close that does not ascend.");
        }

        if (_count == _epochs.Length)
        {
            var size = Math.Max(16, _count * 2);
            Array.Resize(ref _epochs, size);
            Array.Resize(ref _closes, size);
            for (var f = 0; f < _values.Length; f++)
            {
                Array.Resize(ref _values[f], size);
            }
        }

        _epochs[_count] = closeEpoch;
        _closes[_count] = close;
        for (var f = 0; f < _values.Length; f++)
        {
            _values[f][_count] = values[f];
        }

        _count++;
    }

    private int CheckedBar(int bar) => (uint)bar < (uint)_count ? bar : throw new ArgumentOutOfRangeException(nameof(bar));
}
