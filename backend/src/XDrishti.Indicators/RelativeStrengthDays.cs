using System.Globalization;

namespace XDrishti.Indicators;

/// <summary>One stored relative-strength number: the feature it belongs to (kind and its parameters), the last closed session it is as of, and the value (NaN when the stored state is not a value).</summary>
public readonly record struct RelativeStrengthPoint(DateFeatureKind Kind, int P1, int P2, int P3, DateOnly AsOf, double Value);

/// <summary>
/// The stored relative-strength rows of one instrument as the frame reads them (BK-09): per feature a series by <c>as_of</c>. <see cref="Before"/> gives the value of the newest row strictly before a date, so a strategy on date D reads the
/// row of the previous closed session and the frame's own date lane applies no second shift. A date with no earlier row, a feature with no series and a state that is not a value all read NaN (never 0). Immutable; frames share one.
/// </summary>
public sealed class RelativeStrengthDays
{
    /// <summary>No rows: every read is NaN.</summary>
    public static readonly RelativeStrengthDays Empty = new([]);

    private readonly Dictionary<string, (DateOnly[] Dates, double[] Values)> _series;

    public RelativeStrengthDays(IEnumerable<RelativeStrengthPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _series = [];
        foreach (var group in points.GroupBy(p => Key(p.Kind, p.P1, p.P2, p.P3), StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(p => p.AsOf).ToArray();
            _series[group.Key] = ([.. ordered.Select(p => p.AsOf)], [.. ordered.Select(p => p.Value)]);
        }
    }

    /// <summary>The value of the feature as of the newest stored row strictly before <paramref name="date"/>; NaN when there is none.</summary>
    public double Before(DateFeatureKind kind, int p1, int p2, int p3, DateOnly date)
    {
        if (!_series.TryGetValue(Key(kind, p1, p2, p3), out var s))
        {
            return double.NaN;
        }

        var i = Array.BinarySearch(s.Dates, date);
        var before = i >= 0 ? i - 1 : ~i - 1;
        return before >= 0 ? s.Values[before] : double.NaN;
    }

    private static string Key(DateFeatureKind kind, int p1, int p2, int p3) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)kind}:{p1}:{p2}:{p3}");
}
