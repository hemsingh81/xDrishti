namespace XDrishti.Domain.Selection;

/// <summary>
/// Correlation clusters by average linkage on distance 1 - rho, cut where the average pair correlation inside the merged group would fall below <see cref="ClusterOptions.ClusterRho"/> (spec BK-08, ADR 0260). Average, not single, linkage: one 0.72 pair must not chain four
/// unrelated names into one cap. A pair with no figure is unknown and is left out of the average (never 0 or 1); two groups with no known pair between them do not join. A symbol with no known pair at all (among several symbols) is a singleton flagged
/// <see cref="CorrelationCluster.CorrelationUnknown"/>. Pure and deterministic: ids are the smallest member, ties go to the smallest ids, the input order does not matter.
/// </summary>
public static class CorrelationClusters
{
    /// <summary>Clusters the symbols (duplicates ignoring case count once). A null matrix means every pair is unknown.</summary>
    public static ClusterResult Compute(CorrelationMatrix? matrix, IReadOnlyList<string> symbols, ClusterOptions options)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(options);
        var names = symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Select(CorrelationMatrix.Normalize).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var n = names.Count;
        var sum = new double[n, n];
        var count = new int[n, n];
        var knownPairs = new int[n];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (matrix?.Get(names[i], names[j]) is { } rho)
                {
                    sum[i, j] = sum[j, i] = rho;
                    count[i, j] = count[j, i] = 1;
                    knownPairs[i]++;
                    knownPairs[j]++;
                }
            }
        }

        var members = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();
        var alive = Enumerable.Range(0, n).ToList();
        while (alive.Count > 1)
        {
            (int A, int B, double Rho)? best = null;
            foreach (var a in alive)
            {
                foreach (var b in alive.Where(x => x > a))
                {
                    if (count[a, b] == 0)
                    {
                        continue;
                    }

                    var average = Math.Round(sum[a, b] / count[a, b], 9);
                    if (average >= options.ClusterRho && (best is null || average > best.Value.Rho))
                    {
                        best = (a, b, average);
                    }
                }
            }

            if (best is not { } merge)
            {
                break;
            }

            // The group keeps the lower index (its smallest member); sums and counts of the pairs add up (Lance-Williams for the average).
            foreach (var c in alive.Where(x => x != merge.A && x != merge.B))
            {
                sum[merge.A, c] = sum[c, merge.A] = sum[merge.A, c] + sum[merge.B, c];
                count[merge.A, c] = count[c, merge.A] = count[merge.A, c] + count[merge.B, c];
            }

            members[merge.A].AddRange(members[merge.B]);
            alive.Remove(merge.B);
        }

        var clusters = alive.Select(a =>
        {
            var list = members[a].Select(i => names[i]).Order(StringComparer.Ordinal).ToList();
            return new CorrelationCluster(list[0], list, n > 1 && list.Count == 1 && knownPairs[a] == 0);
        }).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        return new ClusterResult(clusters, [.. clusters.Where(c => c.CorrelationUnknown).Select(c => c.Id)]);
    }
}
