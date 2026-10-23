namespace XDrishti.Domain.Learning;

/// <summary>A scorer that can say which features moved one prediction (P6-06). The contributions are path-based attributions in raw-score units, before the calibration map (which is monotone but not linear): they rank the features and point the way of the margin, but they are not exact, not SHAP, and a bias term is not included, so they need not add up to the margin. They are stored with the score as its explanation.</summary>
public interface IExplainableScorer : IRowScorer
{
    /// <summary>The largest contributions by absolute value, at most <paramref name="top"/>, ordered by absolute value (largest first) then by index.</summary>
    IReadOnlyList<FeatureContribution> Explain(ValidationRow row, int top);
}
