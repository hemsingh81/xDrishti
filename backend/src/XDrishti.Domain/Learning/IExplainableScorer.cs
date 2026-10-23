namespace XDrishti.Domain.Learning;

/// <summary>A scorer that can say which features moved one prediction (P6-06). The contributions are in raw-score units, before the calibration map (which is monotone but not linear), and are stored with the score as its explanation.</summary>
public interface IExplainableScorer : IRowScorer
{
    /// <summary>The largest contributions by absolute value, at most <paramref name="top"/>, ordered by absolute value (largest first) then by index.</summary>
    IReadOnlyList<FeatureContribution> Explain(ValidationRow row, int top);
}
