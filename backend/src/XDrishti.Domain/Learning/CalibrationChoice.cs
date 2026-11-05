namespace XDrishti.Domain.Learning;

/// <summary>
/// The calibration is chosen by how many rows there are to fit it on (CF-10 part 10a, ADR 0214, decision D1). No usable row: the constant 0.5. Fewer than <c>isotonic_min_rows</c> (500, a proposal): Platt scaling, two parameters, because an isotonic map can have up to one step
/// per distinct score (80 steps on 80 rows) and fits noise. At least that many: isotonic, which can follow a real non-sigmoid shape once the data show one. Rows are the rows with a finite score. Pure and deterministic.
/// </summary>
public static class CalibrationChoice
{
    /// <summary>The default cut, in calibration rows, at and above which the isotonic map is used.</summary>
    public const int DefaultIsotonicMinRows = Confidence.CalibrationOptions.DefaultIsotonicMinRows;

    /// <summary>The kind for this many calibration rows: none is the base rate, fewer than the cut is Platt, the cut or more is isotonic.</summary>
    public static CalibrationKind KindFor(int rows, int isotonicMinRows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(isotonicMinRows, 1);
        return rows <= 0 ? CalibrationKind.BaseRate : rows >= isotonicMinRows ? CalibrationKind.Isotonic : CalibrationKind.Platt;
    }

    /// <summary>Fits the map of the kind that the number of finite-score rows chooses. Fit it on rows the scorer was not trained on.</summary>
    public static ScoreCalibration Fit(IReadOnlyList<double> scores, IReadOnlyList<int> labels, int isotonicMinRows)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(labels);
        if (scores.Count != labels.Count)
        {
            throw new ArgumentException("scores and labels must have the same length.", nameof(labels));
        }

        var finite = Enumerable.Range(0, scores.Count).Where(i => double.IsFinite(scores[i])).ToArray();
        var kind = KindFor(finite.Length, isotonicMinRows);
        switch (kind)
        {
            case CalibrationKind.Isotonic:
                return ScoreCalibration.FromIsotonic(IsotonicCalibrator.Fit(scores, labels), finite.Length);
            case CalibrationKind.BaseRate:
                return ScoreCalibration.FromPlatt(PlattCalibrator.Constant(0.5), [0], CalibrationKind.BaseRate, 0);
            default:
                var platt = PlattCalibrator.Fit(scores, labels);
                var low = finite.Min(i => scores[i]);
                var high = finite.Max(i => scores[i]);
                return ScoreCalibration.FromPlatt(platt, Curve(low, high), CalibrationKind.Platt, finite.Length);
        }
    }

    /// <summary>The raw scores at which a Platt curve is read: eleven evenly spaced across the range, or the one score when the range is a point.</summary>
    public static IReadOnlyList<double> Curve(double low, double high)
    {
        if (!(high > low))
        {
            return [low];
        }

        var points = new double[11];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = low + ((high - low) * i / (points.Length - 1));
        }

        points[^1] = high;
        return points.Zip(points.Skip(1), (a, b) => b > a).All(x => x) ? points : [low];
    }
}
