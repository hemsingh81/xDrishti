namespace XDrishti.Application.Learning;

/// <summary>The probability of label 1 for one row and the features that moved it.</summary>
/// <param name="ModelId">The model.</param>
/// <param name="Probability">The calibrated probability.</param>
/// <param name="Contributions">The largest path-based contributions in raw-score units (not exact, not SHAP, no bias term), largest first.</param>
public sealed record ModelScoreView(long ModelId, double Probability, IReadOnlyList<FeatureImportanceView> Contributions);
