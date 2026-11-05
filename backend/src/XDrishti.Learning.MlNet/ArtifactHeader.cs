using CalibrationKinds = XDrishti.Domain.Learning.CalibrationKindExtensions;

namespace XDrishti.Learning.MlNet;

/// <summary>The part of an artifact that is not the ML.NET model (P6-06): everything needed to turn a row into a probability and to say how the model was made.</summary>
/// <param name="Version">The format version: 1 for an isotonic calibration (the format before CF-10, so its bytes and hash are unchanged), 2 for a Platt or base-rate calibration (an older build refuses it instead of misreading it as isotonic).</param>
/// <param name="Trainer">The trainer that was used.</param>
/// <param name="Width">The number of features.</param>
/// <param name="Means">The imputation value per feature: the mean of the fit rows.</param>
/// <param name="CalibrationScores">The calibration curve, raw scores: the isotonic knots, or points of the Platt sigmoid (CF-10 part 10a).</param>
/// <param name="CalibrationValues">The calibration curve, probabilities.</param>
/// <param name="ProbabilityFloor">The clamp of the calibrated probability.</param>
/// <param name="FitRows">Rows the trees were fitted on.</param>
/// <param name="CalibrationRows">Rows the calibration was fitted on.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Note">What a reader must know about the fit (for example the fall-back to FastTree and why), or null.</param>
/// <param name="CalibrationKind">How the raw score becomes a probability: <c>isotonic</c> (also what an artifact written before CF-10 means), <c>platt</c> or <c>base_rate</c> (CF-10 part 10a, ADR 0214).</param>
/// <param name="PlattSlope">The Platt slope (0 for the other kinds).</param>
/// <param name="PlattCenter">The Platt centring constant.</param>
/// <param name="PlattIntercept">The Platt intercept (the logit of the constant for <c>base_rate</c>).</param>
public sealed record ArtifactHeader(
    int Version,
    TreeTrainer Trainer,
    int Width,
    double[] Means,
    double[] CalibrationScores,
    double[] CalibrationValues,
    double ProbabilityFloor,
    int FitRows,
    int CalibrationRows,
    int Seed,
    string? Note,
    string CalibrationKind = "isotonic",
    double PlattSlope = 0,
    double PlattCenter = 0,
    double PlattIntercept = 0)
{
    /// <summary>True when every field is present, finite and in range, so that the loader can use it without a surprise. The width against the model itself is checked after the model is unpacked.</summary>
    internal bool IsUsable() =>
        (Version == 1 ? CalibrationKinds.FromCode(CalibrationKind) == Domain.Learning.CalibrationKind.Isotonic : Version == 2 && CalibrationKinds.FromCode(CalibrationKind) is { } kind && kind != Domain.Learning.CalibrationKind.Isotonic)
        && Enum.IsDefined(Trainer)
        && Width is >= 1 and <= 100000
        && Means is { } means && means.Length == Width && means.All(double.IsFinite)
        && CalibrationScores is { Length: > 0 } scores && CalibrationValues is { } values && values.Length == scores.Length && scores.All(double.IsFinite) && values.All(double.IsFinite)
        && double.IsFinite(PlattSlope) && PlattSlope >= 0 && double.IsFinite(PlattCenter) && double.IsFinite(PlattIntercept)
        && double.IsFinite(ProbabilityFloor) && ProbabilityFloor is > 0 and < 0.5
        && FitRows >= 0 && CalibrationRows >= 0;
}
