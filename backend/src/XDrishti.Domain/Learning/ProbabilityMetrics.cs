namespace XDrishti.Domain.Learning;

/// <summary>The scores of predicted probabilities against labels (P6-05). Lower is better for <see cref="LogLoss"/>, <see cref="Brier"/> and <see cref="EcePp"/>.</summary>
/// <param name="N">Rows scored.</param>
/// <param name="Positives">Rows with label 1.</param>
/// <param name="LogLoss">Mean negative log likelihood, probabilities clamped to [1e-15, 1 - 1e-15].</param>
/// <param name="Brier">Mean squared error of the probability.</param>
/// <param name="Auc">Area under the ROC curve (ties share their mean rank); null when one class is missing.</param>
/// <param name="EcePp">Expected calibration error in percentage points over equal-mass bins.</param>
/// <param name="Lift">Win rate of the top fifth by score over the overall win rate; null when undefined.</param>
public sealed record ProbabilityMetrics(int N, int Positives, double LogLoss, double Brier, double? Auc, double EcePp, double? Lift);
