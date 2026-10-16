namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// The edit distance of the tests, written separately from the library's: the full matrix of the optimal string alignment distance (insert, delete, replace, swap two neighbours; each costs 1), compared without
/// regard to case. The library keeps two rows of it; a mistake in one is not repeated in the other.
/// </summary>
internal static class EditDistance
{
    public static int Between(string first, string second)
    {
        var a = first.ToLowerInvariant();
        var b = second.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var replace = d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                d[i, j] = Math.Min(replace, Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}
