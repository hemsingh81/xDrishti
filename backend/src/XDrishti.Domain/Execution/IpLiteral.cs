using System.Globalization;

namespace XDrishti.Domain.Execution;

/// <summary>
/// A static IP as the owner states it (spec P12-11): one IPv4 literal in dotted decimal form or one IPv6 literal, nothing else. No host name, no port, no prefix length, no scope id, no blank, no octal or short form. The normal form is what is stored and compared, so the same
/// address written two ways is one address. Parsed by hand on purpose: the domain stays free of any networking type.
/// </summary>
public static class IpLiteral
{
    public const int MaxLength = 45;

    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        if (!text.Contains(':', StringComparison.Ordinal))
        {
            return TryFour(text, out var four) && Set(string.Join('.', four), out normalized);
        }

        return TrySix(text, out var groups) && Set(Compress(groups), out normalized);
    }

    private static bool Set(string value, out string normalized)
    {
        normalized = value;
        return true;
    }

    private static bool TryFour(string text, out int[] octets)
    {
        var parts = text.Split('.');
        octets = new int[4];
        if (parts.Length != 4)
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            var p = parts[i];
            if (p.Length is 0 or > 3 || !p.All(char.IsAsciiDigit) || (p.Length > 1 && p[0] == '0') || !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out octets[i]) || octets[i] > 255)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySix(string text, out ushort[] groups)
    {
        groups = new ushort[8];
        var gap = text.IndexOf("::", StringComparison.Ordinal);
        if (gap >= 0 && text.IndexOf("::", gap + 1, StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        var head = gap < 0 ? text : text[..gap];
        var tail = gap < 0 ? string.Empty : text[(gap + 2)..];
        if (!TryList(head, gap < 0, out var first) || !TryList(tail, true, out var second))
        {
            return false;
        }

        if (gap < 0 ? first.Count != 8 : first.Count + second.Count > 7)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            groups[i] = first[i];
        }

        for (var i = 0; i < second.Count; i++)
        {
            groups[8 - second.Count + i] = second[i];
        }

        return true;
    }

    // A run of colon-separated groups; the last one may be a dotted IPv4 (two groups) when this run ends the address.
    private static bool TryList(string part, bool mayEndWithFour, out List<ushort> list)
    {
        list = [];
        if (part.Length == 0)
        {
            return true;
        }

        var items = part.Split(':');
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item.Contains('.', StringComparison.Ordinal))
            {
                if (!mayEndWithFour || i != items.Length - 1 || !TryFour(item, out var o))
                {
                    return false;
                }

                list.Add((ushort)((o[0] << 8) | o[1]));
                list.Add((ushort)((o[2] << 8) | o[3]));
                continue;
            }

            if (item.Length is 0 or > 4 || !item.All(char.IsAsciiHexDigit) || !ushort.TryParse(item, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var g))
            {
                return false;
            }

            list.Add(g);
        }

        return true;
    }

    // Lower-case hex without leading zeros; the longest run of two or more zero groups (the first of equal ones) becomes "::".
    private static string Compress(ushort[] g)
    {
        int bestStart = -1, bestLen = 0;
        for (var i = 0; i < 8;)
        {
            if (g[i] != 0)
            {
                i++;
                continue;
            }

            var j = i;
            while (j < 8 && g[j] == 0)
            {
                j++;
            }

            if (j - i > bestLen)
            {
                (bestStart, bestLen) = (i, j - i);
            }

            i = j;
        }

        if (bestLen < 2)
        {
            return string.Join(':', g.Select(x => x.ToString("x", CultureInfo.InvariantCulture)));
        }

        var left = string.Join(':', g.Take(bestStart).Select(x => x.ToString("x", CultureInfo.InvariantCulture)));
        var right = string.Join(':', g.Skip(bestStart + bestLen).Select(x => x.ToString("x", CultureInfo.InvariantCulture)));
        return left + "::" + right;
    }
}
