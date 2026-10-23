namespace XDrishti.Application.Learning;

/// <summary>The weight of one feature in a model or in one score: a path-based contribution in raw-score units (not exact, not SHAP).</summary>
/// <param name="Feature">The feature name, or <c>f&lt;index&gt;</c> when the catalog of the model is not the current one.</param>
/// <param name="MeanAbsContribution">For a model the mean absolute contribution over its training rows; for one score the signed contribution.</param>
public sealed record FeatureImportanceView(string Feature, double MeanAbsContribution);
