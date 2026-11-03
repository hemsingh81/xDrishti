namespace XDrishti.Domain.Pairs;

/// <summary>
/// Benjamini-Hochberg false-discovery control (spec P14-12): sort the p-values ascending (ties by input index), find the largest <c>k</c> with <c>p_(k) &lt;= k q / m</c>, reject the first <c>k</c>.
/// An unavailable test is counted in <c>m</c> with <c>p = 1</c>: it can never be rejected and it widens every threshold (conservative); it is never dropped.
/// </summary>
public static class BenjaminiHochberg
{
    /// <summary>On plain p-values; a value that is not a number in [0, 1] counts as <c>p = 1</c>. <paramref name="q"/> outside (0, 1] is <c>bad_input</c>.</summary>
    public static PairsResult<BhOutcome> Reject(ReadOnlySpan<double> pValues, double q)
    {
        if (!double.IsFinite(q) || q <= 0 || q > 1)
        {
            return PairsResults.Unavailable<BhOutcome>(PairsReason.BadInput);
        }

        var m = pValues.Length;
        var p = new double[m];
        var order = new int[m];
        for (var i = 0; i < m; i++)
        {
            p[i] = double.IsFinite(pValues[i]) && pValues[i] >= 0 && pValues[i] <= 1 ? pValues[i] : 1.0;
            order[i] = i;
        }

        Array.Sort(order, (a, b) => p[a] != p[b] ? p[a].CompareTo(p[b]) : a.CompareTo(b));
        var k = 0;
        for (var rank = m; rank >= 1; rank--)
        {
            // p <= rank q / m written as p m <= rank q, with a relative slack of 1e-12 so that a value exactly on the line (0.04 at 4 q / 5) is not lost to rounding.
            if (p[order[rank - 1]] * m <= rank * q * (1.0 + 1e-12))
            {
                k = rank;
                break;
            }
        }

        var rejected = new bool[m];
        for (var r = 0; r < k; r++)
        {
            rejected[order[r]] = true;
        }

        return PairsResults.Of(new BhOutcome(rejected, k, m));
    }

    /// <summary>On test results: an unavailable test is counted with <c>p = 1</c>.</summary>
    public static PairsResult<BhOutcome> Reject(IReadOnlyList<PairsResult<EngleGrangerResult>> tests, double q)
    {
        ArgumentNullException.ThrowIfNull(tests);
        var p = new double[tests.Count];
        for (var i = 0; i < p.Length; i++)
        {
            p[i] = tests[i].TryGet(out var t) ? t.PValue : 1.0;
        }

        return Reject(p, q);
    }
}
