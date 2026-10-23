namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A trained model: its file bytes with the hash, how it was made and the tables a reader needs.</summary>
/// <param name="Bytes">The artifact.</param>
/// <param name="Sha256">Its hash (lower-case hex).</param>
/// <param name="Trainer">FastTree or LightGbm.</param>
/// <param name="Note">What a reader must know about the fit, or null.</param>
/// <param name="Width">Number of features.</param>
/// <param name="FitRows">Rows the trees were fitted on.</param>
/// <param name="CalibrationRows">Rows the calibration was fitted on.</param>
/// <param name="CalibrationScores">The calibration table, raw scores.</param>
/// <param name="CalibrationValues">The calibration table, probabilities.</param>
/// <param name="Importance">Mean absolute contribution per feature over the rows the model was trained on.</param>
public sealed record TrainedModel(
    byte[] Bytes,
    string Sha256,
    string Trainer,
    string? Note,
    int Width,
    int FitRows,
    int CalibrationRows,
    IReadOnlyList<double> CalibrationScores,
    IReadOnlyList<double> CalibrationValues,
    IReadOnlyList<double> Importance);
