namespace XDrishti.Application.Learning;

/// <summary>One point of the calibration table of a model: a raw score and the probability it maps to.</summary>
/// <param name="Score">The raw score of the trees.</param>
/// <param name="Probability">The calibrated probability.</param>
public sealed record CalibrationPoint(double Score, double Probability);
