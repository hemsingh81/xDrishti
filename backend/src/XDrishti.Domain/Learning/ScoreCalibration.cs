namespace XDrishti.Domain.Learning;

/// <summary>
/// The map from a raw score to a probability that a model uses (CF-10 part 10a, ADR 0214): isotonic, Platt or the constant base rate, chosen by <see cref="CalibrationChoice"/>. <see cref="Scores"/> and <see cref="Values"/> are the curve a reader plots: the knots of an isotonic map,
/// or eleven points of a Platt sigmoid across the fitted score range (one point for a constant). The map is non-decreasing in every kind.
/// </summary>
public sealed class ScoreCalibration
{
    private readonly IsotonicCalibrator? _isotonic;
    private readonly PlattCalibrator? _platt;

    private ScoreCalibration(CalibrationKind kind, IsotonicCalibrator? isotonic, PlattCalibrator? platt, double[] scores, double[] values, int rows)
    {
        Kind = kind;
        _isotonic = isotonic;
        _platt = platt;
        Scores = scores;
        Values = values;
        Rows = rows;
    }

    /// <summary>How the score is calibrated.</summary>
    public CalibrationKind Kind { get; }

    /// <summary>The curve's raw scores, strictly increasing.</summary>
    public IReadOnlyList<double> Scores { get; }

    /// <summary>The curve's probabilities, non-decreasing.</summary>
    public IReadOnlyList<double> Values { get; }

    /// <summary>The calibration rows the map was fitted on (0 when it was loaded from a stored artifact that does not say).</summary>
    public int Rows { get; }

    /// <summary>The Platt parameters, or null for the other kinds. A base-rate map has slope 0.</summary>
    public PlattCalibrator? Platt => _platt;

    /// <summary>The map from isotonic knots (also what a model stored before CF-10 holds).</summary>
    public static ScoreCalibration FromIsotonic(IsotonicCalibrator isotonic, int rows = 0)
    {
        ArgumentNullException.ThrowIfNull(isotonic);
        return new ScoreCalibration(CalibrationKind.Isotonic, isotonic, null, [.. isotonic.Scores], [.. isotonic.Values], rows);
    }

    /// <summary>A Platt sigmoid (or, with <paramref name="kind"/> <see cref="CalibrationKind.BaseRate"/>, a constant) with the raw scores at which its curve is read; the scores must be non-empty and strictly increasing.</summary>
    public static ScoreCalibration FromPlatt(PlattCalibrator platt, IReadOnlyList<double> curveScores, CalibrationKind kind = CalibrationKind.Platt, int rows = 0)
    {
        ArgumentNullException.ThrowIfNull(platt);
        ArgumentNullException.ThrowIfNull(curveScores);
        if (kind == CalibrationKind.Isotonic)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (curveScores.Count == 0 || curveScores.Any(s => !double.IsFinite(s)) || curveScores.Zip(curveScores.Skip(1), (a, b) => b <= a).Any(x => x))
        {
            throw new ArgumentException("The curve scores must be finite, non-empty and strictly increasing.", nameof(curveScores));
        }

        return new ScoreCalibration(kind, null, platt, [.. curveScores], [.. curveScores.Select(platt.Apply)], rows);
    }

    /// <summary>The calibrated probability of a raw score.</summary>
    public double Apply(double score) => _isotonic is { } iso ? iso.Apply(score) : _platt!.Apply(score);
}
