using System.Text;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// How scrip names are compared (ADR 0028). <see cref="Key"/> reduces a name to its words: lower case, punctuation gone (dots and apostrophes vanish, any other mark separates words), <c>&amp;</c> read as <c>and</c>, and the corporate
/// suffixes <c>Ltd, Limited, Corporation, Corp, India, Industries, Inc, Plc, Pvt, Private, Company</c> dropped, so <c>Power Finance Corporation Ltd.</c> and <c>POWER FINANCE CORPORATION</c> are one key. A name made only of such words keeps them.
/// <see cref="Contains"/> says whether one key lies inside another as whole words, which is how a partial name is recognised — but only when the shorter key has at least two significant words (not <c>of</c>, <c>and</c>, <c>the</c>, <c>for</c>,
/// <c>in</c>), so <c>Bank of India</c> (<c>bank of</c>) never matches <c>Union Bank of India</c>. Pure.
/// <para>
/// <b>Changing these rules changes the identity of every dividend stored from a file</b> (the identity hashes the key, see <see cref="DividendIdentity"/>) and the keys of remembered names (aliases): the same file would be imported again as new
/// payments. <c>DividendIdentityGoldenTests</c> pins keys and identities and fails on such a change.
/// </para>
/// </summary>
internal static class DividendNames
{
    /// <summary>The longest key; a longer name is cut (the key is stored in aliases).</summary>
    public const int MaxKeyLength = 200;

    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "ltd", "limited", "corporation", "corp", "india", "industries", "inc", "plc", "pvt", "private", "company",
    };

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal) { "of", "and", "the", "for", "in" };

    /// <summary>The key of a name; empty when the name has no letters or digits.</summary>
    public static string Key(string? name)
    {
        var words = Words(name);
        var kept = words.Where(w => !Suffixes.Contains(w)).ToList();
        var key = string.Join(' ', kept.Count > 0 ? kept : words);
        return key.Length <= MaxKeyLength ? key : key[..MaxKeyLength].TrimEnd();
    }

    /// <summary>True when the shorter key is inside the longer one as whole, consecutive words and has at least two significant words. Equal keys are not "contained" (equality is a rule of its own).</summary>
    public static bool Contains(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0 || string.Equals(first, second, StringComparison.Ordinal))
        {
            return false;
        }

        var (longer, shorter) = first.Length >= second.Length ? (first, second) : (second, first);
        var inner = shorter.Split(' ');
        if (inner.Count(w => !Fillers.Contains(w)) < 2)
        {
            return false;
        }

        var outer = longer.Split(' ');
        for (var start = 0; start + inner.Length <= outer.Length; start++)
        {
            var same = true;
            for (var i = 0; i < inner.Length && same; i++)
            {
                same = string.Equals(outer[start + i], inner[i], StringComparison.Ordinal);
            }

            if (same)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> Words(string? name)
    {
        var text = new StringBuilder((name?.Length ?? 0) + 8);
        foreach (var c in (name ?? string.Empty).ToLowerInvariant())
        {
            if (c == '&')
            {
                text.Append(" and ");
            }
            else if (c is '.' or '\'' or '’' or '`')
            {
                // Dots and apostrophes vanish: "N.M.D.C." is "nmdc", "Nestle's" is "nestles".
            }
            else
            {
                text.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
        }

        return [.. text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }
}
