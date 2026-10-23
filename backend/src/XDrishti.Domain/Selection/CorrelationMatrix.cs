namespace XDrishti.Domain.Selection;

/// <summary>Pair correlations between symbols, passed in (computed elsewhere, P7-06). A pair that is absent, or not a number from -1 to 1, is unknown. If a pair is given twice the higher value is kept (safe side).</summary>
public sealed class CorrelationMatrix
{
    private readonly Dictionary<(string, string), double> _values = [];

    /// <summary>Builds the matrix.</summary>
    public CorrelationMatrix(IEnumerable<(string A, string B, double Value)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        foreach (var (a, b, v) in pairs)
        {
            if (!double.IsFinite(v) || v is < -1 or > 1 || string.Equals(a, b, StringComparison.Ordinal))
            {
                continue;
            }

            var key = Key(a, b);
            if (!_values.TryGetValue(key, out var old) || v > old)
            {
                _values[key] = v;
            }
        }
    }

    /// <summary>The correlation of two symbols; null when unknown.</summary>
    public double? Get(string a, string b) => _values.TryGetValue(Key(a, b), out var v) ? v : null;

    private static (string, string) Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
}
