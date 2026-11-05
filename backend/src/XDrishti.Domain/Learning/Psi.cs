using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The population stability index of a feature (CF-10 part 10b, ADR 0215, decision D3): <c>sum((a - e) x ln(a / e))</c> over equal-mass bins of the training distribution, <c>e</c> the training share of a bin and <c>a</c> the share of the recent rows.
/// Hand check: <c>e = (0.5, 0.5)</c>, <c>a = (0.7, 0.3)</c> gives <c>0.2 ln 1.4 - 0.2 ln 0.6 = 0.067294 + 0.102165 = 0.1695</c>, "watch". A share of zero is lifted to <see cref="Floor"/> so the logarithm exists (the usual convention).
/// The cuts 0.10 and 0.25 are the standard ones, proposals here. Pure and deterministic; it is evidence, not a rule.
/// </summary>
public static class Psi
{
    /// <summary>The smallest share used in the logarithm.</summary>
    public const double Floor = 1e-4;

    /// <summary>
    /// The fewest rows per bin on the smaller side. The index of two samples from the very same distribution is not zero: it is about <c>(bins - 1) x (1 / n_train + 1 / n_recent)</c>, so with 10 bins and a quarterly test window of 90 rows it sits near 0.1 and
    /// often passes the "alarm" cut of 0.25 by chance. Using fewer bins on small samples keeps that noise well below the cuts (a proposal; the standard cuts were set for large samples).
    /// </summary>
    public const int MinRowsPerBin = 20;

    /// <summary>The index of two lists of bin shares of the same length (each share 0 to 1).</summary>
    public static double Index(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Count == 0 || expected.Count != actual.Count)
        {
            throw new ArgumentException("expected and actual need the same non-zero number of bins.", nameof(actual));
        }

        var sum = 0.0;
        for (var i = 0; i < expected.Count; i++)
        {
            if (!double.IsFinite(expected[i]) || !double.IsFinite(actual[i]) || expected[i] < 0 || actual[i] < 0)
            {
                throw new ArgumentException("a bin share must be a number of at least 0.", nameof(actual));
            }

            var e = Math.Max(expected[i], Floor);
            var a = Math.Max(actual[i], Floor);
            sum += (a - e) * Math.Log(a / e);
        }

        return sum;
    }

    /// <summary>
    /// The index of the recent values against the training values, or null when it cannot be read: fewer than <paramref name="minRows"/> finite values on either side, or a training feature that never changes (then no bin edge exists; a feature with two values is read whichever value is the minority). Bin edges are training
    /// quantiles, so each bin holds the same share of the training rows (fewer bins when values repeat, and no more than the smaller side can fill with <see cref="MinRowsPerBin"/> rows each, at least 2); non-finite values are left out.
    /// </summary>
    public static double? Compute(IReadOnlyList<double> train, IReadOnlyList<double> recent, int bins, int minRows)
    {
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(minRows, 1);
        var sorted = train.Where(double.IsFinite).Order().ToArray();
        var now = recent.Where(double.IsFinite).ToArray();
        if (sorted.Length < minRows || now.Length < minRows)
        {
            return null;
        }

        var edges = Edges(sorted, Math.Min(bins, Math.Max(2, Math.Min(sorted.Length, now.Length) / MinRowsPerBin)));
        if (edges.Count == 0)
        {
            return null;
        }

        return Index(Shares(sorted, edges), Shares(now, edges));
    }

    /// <summary>The level of an index under the settings: below the watch cut stable, up to and including the alarm cut watch, above it alarm.</summary>
    public static PsiLevel Level(double psi, LadderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return psi > options.PsiAlarm ? PsiLevel.Alarm : psi >= options.PsiWatch ? PsiLevel.Watch : PsiLevel.Stable;
    }

    /// <summary>
    /// The bin edges: the training values at which a share of <c>k / bins</c> of the rows is at or below, kept when below the largest value and without repeats; a feature with no more distinct values than bins gets one bin per value. Bins are upper-inclusive: a value goes up a bin when it is above the edge, so a two-valued
    /// feature whose larger value is the minority (80 zeros, 20 ones) has the edge at its lower value and is read. Every bin holds at least one training row.
    /// </summary>
    private static List<double> Edges(double[] sorted, int bins)
    {
        var edges = new List<double>();
        var distinct = sorted.Distinct().ToArray();
        if (distinct.Length <= bins)
        {
            // few values (a flag, a sign, a count): each value is its own bin, whichever is the minority
            return [.. distinct.Take(distinct.Length - 1)];
        }

        for (var k = 1; k < bins; k++)
        {
            var edge = sorted[(int)((((long)k * sorted.Length) + bins - 1) / bins) - 1];
            if (edge < sorted[^1] && (edges.Count == 0 || edge > edges[^1]))
            {
                edges.Add(edge);
            }
        }

        return edges;
    }

    private static double[] Shares(double[] values, List<double> edges)
    {
        var counts = new double[edges.Count + 1];
        foreach (var v in values)
        {
            var bin = 0;
            while (bin < edges.Count && v > edges[bin])
            {
                bin++;
            }

            counts[bin]++;
        }

        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] /= values.Length;
        }

        return counts;
    }
}
