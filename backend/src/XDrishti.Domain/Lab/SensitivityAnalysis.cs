namespace XDrishti.Domain.Lab;

/// <summary>
/// Judges a one-at-a-time sweep (spec P5-05, assumption 5). <c>stable</c>: the base mean R is above 0 and every valid point has mean R above 0 and at least <see cref="KeepFraction"/> of the base mean. <c>fragile</c>: the base has an edge and a point
/// breaks either. <c>no_base_edge</c>: the base mean R is not above 0. <c>inconclusive</c>: no base result, or fewer than <see cref="MinValidPoints"/> valid points. The figures are in-sample. Pure.
/// </summary>
public static class SensitivityAnalysis
{
    /// <summary>The share of the base mean R every valid point must keep to be called stable.</summary>
    public const double KeepFraction = 0.5;

    /// <summary>The fewest valid points of a parameter for a verdict.</summary>
    public const int MinValidPoints = 2;

    public static IReadOnlyList<ParameterSensitivity> Judge(SweepResult? baseline, IReadOnlyList<SweepResult> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return [.. points.GroupBy(p => p.Point.Parameter, StringComparer.Ordinal).Select(g => Judge(g.Key, baseline, [.. g]))];
    }

    private static ParameterSensitivity Judge(string parameter, SweepResult? baseline, IReadOnlyList<SweepResult> points)
    {
        var valid = points.Where(p => p.Error is null && p.MeanR is not null).ToList();
        if (baseline?.MeanR is not { } baseMean)
        {
            return new ParameterSensitivity(parameter, points, "inconclusive", "The base run gave no result" + (baseline?.Error is { } e ? ": " + e : "."));
        }

        if (valid.Count < MinValidPoints)
        {
            return new ParameterSensitivity(parameter, points, "inconclusive", valid.Count + " valid point(s); " + MinValidPoints + " are needed.");
        }

        if (baseMean <= 0)
        {
            return new ParameterSensitivity(parameter, points, "no_base_edge", "The base mean R is not above 0, so there is no edge for the parameter to hold.");
        }

        var weak = valid.Count(p => p.MeanR <= 0 || p.MeanR < KeepFraction * baseMean);
        var unusable = points.Count - valid.Count;
        if (weak == 0 && unusable > 0)
        {
            return new ParameterSensitivity(parameter, points, "inconclusive", unusable + " of " + points.Count + " points gave no trades or were refused; a plateau cannot be claimed.");
        }

        return weak == 0
            ? new ParameterSensitivity(parameter, points, "stable", "Every valid point keeps a positive mean R of at least half the base (in-sample).")
            : new ParameterSensitivity(parameter, points, "fragile", weak + " of " + valid.Count + " valid points lose the edge or fall below half the base mean R (in-sample).");
    }
}
