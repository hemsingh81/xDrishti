namespace XDrishti.Domain.Selection;

/// <summary>How correlation clusters are cut (spec BK-08, ADR 0260). Pairs with too few common sessions are already absent from the <see cref="CorrelationMatrix"/> (P7-06 keeps 30), so they are unknown here.</summary>
/// <param name="ClusterRho">Groups join while their average pair correlation is at least this; the design's pair limit 0.70 (doc 10 section 5).</param>
public sealed record ClusterOptions(double ClusterRho = 0.70)
{
    /// <summary>True when the cut is a number from -1 to 1.</summary>
    public bool IsValid => double.IsFinite(ClusterRho) && ClusterRho is >= -1 and <= 1;
}
