using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>
/// The numbers and clock times in a text (spec P9-07 AC 3 C6, test plan). A token is a clock time (<c>9:45</c>, <c>09:45</c> and <c>15:15</c> are times; a one-digit second part such as <c>1:2</c> is a ratio) or a plain number; years and the 15 of "15-min" count. Tokens are normalised so
/// that <c>09:45</c> equals <c>9:45</c> and <c>0.50</c> equals <c>0.5</c>.
/// </summary>
public static partial class DraftIdea
{
    [GeneratedRegex(@"(?<![\d.:])(\d{1,2}):(\d{1,2})(?![\d:])|(?<![\d.])\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    /// <summary>The distinct normalised tokens in order of appearance.</summary>
    public static IReadOnlyList<string> Numbers(string? text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (Match m in Token().Matches(text ?? string.Empty))
        {
            var token = Normalise(m);
            if (seen.Add(token))
            {
                list.Add(token);
            }
        }

        return list;
    }

    /// <summary>The same tokens as a set, with the two parts of a ratio and of a time also present as plain numbers, for looking things up.</summary>
    public static HashSet<string> Haystack(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Token().Matches(text ?? string.Empty))
        {
            set.Add(Normalise(m));
            if (m.Groups[1].Success)
            {
                set.Add(Plain(m.Groups[1].Value));
                set.Add(Plain(m.Groups[2].Value));
            }
        }

        return set;
    }

    /// <summary>Whether the idea token is a ratio such as 1:2 (a one-digit second part).</summary>
    public static bool IsRatio(string token) => token.Length >= 3 && token[^2] == ':' && token.Count(c => c == ':') == 1;

    /// <summary>The number after the colon of a ratio token.</summary>
    public static string RatioSecond(string token) => token[(token.IndexOf(':', StringComparison.Ordinal) + 1)..];

    private static string Normalise(Match m)
    {
        if (m.Groups[1].Success)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Plain(m.Groups[1].Value)}:{m.Groups[2].Value}");
        }

        return Plain(m.Value);
    }

    private static string Plain(string value)
    {
        if (value.Contains('.', StringComparison.Ordinal))
        {
            var t = value.TrimStart('0').TrimEnd('0').TrimEnd('.');
            return t.StartsWith('.') ? "0" + t : t.Length == 0 ? "0" : t;
        }

        var u = value.TrimStart('0');
        return u.Length == 0 ? "0" : u;
    }
}
