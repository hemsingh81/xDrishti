namespace XDrishti.Domain.Selection;

/// <summary>The clusters of a set of symbols, ordered by id, and the symbols whose correlation is unknown.</summary>
/// <param name="Clusters">Every symbol is in exactly one cluster.</param>
/// <param name="Unknown">The symbols flagged <c>correlation_unknown</c>, ascending.</param>
public sealed record ClusterResult(IReadOnlyList<CorrelationCluster> Clusters, IReadOnlyList<string> Unknown)
{
    /// <summary>The cluster of a symbol (compared ignoring case and blanks), or null when the symbol was not clustered.</summary>
    public CorrelationCluster? Of(string symbol) => Clusters.FirstOrDefault(c => c.Members.Contains(CorrelationMatrix.Normalize(symbol), StringComparer.Ordinal));
}
