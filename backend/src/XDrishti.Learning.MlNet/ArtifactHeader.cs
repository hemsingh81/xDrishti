namespace XDrishti.Learning.MlNet;

/// <summary>The part of an artifact that is not the ML.NET model (P6-06): everything needed to turn a row into a probability and to say how the model was made.</summary>
/// <param name="Version">The format version (1).</param>
/// <param name="Trainer">The trainer that was used.</param>
/// <param name="Width">The number of features.</param>
/// <param name="Means">The imputation value per feature: the mean of the fit rows.</param>
/// <param name="CalibrationScores">The calibration knots, raw scores.</param>
/// <param name="CalibrationValues">The calibration knots, probabilities.</param>
/// <param name="ProbabilityFloor">The clamp of the calibrated probability.</param>
/// <param name="FitRows">Rows the trees were fitted on.</param>
/// <param name="CalibrationRows">Rows the calibration was fitted on.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Note">What a reader must know about the fit (for example the fall-back to FastTree and why), or null.</param>
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
    string? Note);
