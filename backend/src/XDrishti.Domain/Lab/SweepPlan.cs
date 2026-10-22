using System.Globalization;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The points of a one-at-a-time sensitivity sweep (spec P5-05, assumption 3): the first declared parameters, each at the declared value times each step; a whole-number parameter is rounded; a point equal to the declared value, to an earlier point of
/// the same parameter, or not a positive finite number is dropped. At most <see cref="MaxParameters"/> parameters and <see cref="MaxPoints"/> points. Pure.
/// </summary>
public static class SweepPlan
{
    /// <summary>The most parameters a sweep takes.</summary>
    public const int MaxParameters = 4;

    /// <summary>The most points a sweep runs.</summary>
    public const int MaxPoints = 16;

    /// <summary>The names of the parameters a sweep takes, in order, whether or not any of their points survives.</summary>
    public static IReadOnlyList<string> Swept(IReadOnlyList<KeyValuePair<string, double>> declared, int maxParameters)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return [.. declared.Take(Math.Min(maxParameters, MaxParameters)).Select(d => d.Key)];
    }

    public static IReadOnlyList<SweepPoint> Build(IReadOnlyList<KeyValuePair<string, double>> declared, int maxParameters, IReadOnlyList<double> steps)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(steps);
        var points = new List<SweepPoint>();
        foreach (var (name, value) in declared.Take(Math.Min(maxParameters, MaxParameters)))
        {
            var whole = value == Math.Round(value);
            var seen = new HashSet<string> { value.ToString("R", CultureInfo.InvariantCulture) };
            foreach (var step in steps)
            {
                var v = value * step;
                v = whole ? Math.Round(v, MidpointRounding.AwayFromZero) : Math.Round(v, 6, MidpointRounding.AwayFromZero);
                if (!double.IsFinite(v) || v <= 0 || !seen.Add(v.ToString("R", CultureInfo.InvariantCulture)))
                {
                    continue;
                }

                points.Add(new SweepPoint(name, v));
                if (points.Count == MaxPoints)
                {
                    return points;
                }
            }
        }

        return points;
    }
}
