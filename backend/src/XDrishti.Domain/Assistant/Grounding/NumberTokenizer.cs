using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>
/// Finds the figures of a text (spec P9-03 AC 6). A figure is a number that claims a quantity: dates, times, list numbers, <c>[n]</c> markers, call references, timeframes (<c>15m</c>, <c>1D</c>, <c>250d</c>), identifiers (<c>ORB15</c>, <c>NIFTY50</c>, <c>v10.0.13</c>) and years are not.
/// It understands ₹, Rs, a unicode minus, Western and Indian grouping, percent, k, L (lakh) and Cr (crore). The same scan reads the numbers of a tool result (<see cref="Sources"/>), with the filters that only make sense for prose left out. Pure; never changes the text.
/// </summary>
public static partial class NumberTokenizer
{
    private const string Months = "Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";

    /// <summary>The figures of a text, in order, non-overlapping.</summary>
    public static IReadOnlyList<NumberToken> Figures(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mask = new bool[text.Length];
        MaskStructure(text, mask);
        Mask(Markers(), text, mask);
        Mask(CallReferences(), text, mask);
        Mask(ListNumbers(), text, mask);
        return Scan(text, mask, figures: true);
    }

    /// <summary>Every number a tool result (or a question, or an argument) holds, as values; a grouped number such as <c>1,234</c> is read both as one number and as its parts (a JSON list <c>[1,234]</c> has two).</summary>
    public static IReadOnlyList<ParsedNumber> Sources(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mask = new bool[text.Length];
        MaskStructure(text, mask);
        var found = new List<ParsedNumber>();
        foreach (var token in Scan(text, mask, figures: false))
        {
            if (token.Number is { } number)
            {
                found.Add(number);
            }

            if (token.Text.Contains(',', StringComparison.Ordinal))
            {
                foreach (var part in token.Text.Split(','))
                {
                    var digits = DigitsRun().Match(part);
                    if (digits.Success && NumberNormaliser.Parse(digits.Value, string.Empty) is { } piece)
                    {
                        found.Add(piece);
                    }
                }
            }
        }

        return found;
    }

    private static void MaskStructure(string text, bool[] mask)
    {
        Mask(IsoDates(), text, mask);
        Mask(SlashDates(), text, mask);
        Mask(TextDates(), text, mask);
        Mask(Times(), text, mask);
        Mask(Versions(), text, mask);
        Mask(Identifiers(), text, mask);
    }

    private static void Mask(Regex pattern, string text, bool[] mask)
    {
        foreach (var match in pattern.EnumerateMatches(text))
        {
            Array.Fill(mask, true, match.Index, match.Length);
        }
    }

    private static List<NumberToken> Scan(string text, bool[] mask, bool figures)
    {
        var tokens = new List<NumberToken>();
        foreach (Match match in Candidate().Matches(text))
        {
            var start = match.Index;
            var end = start + match.Length;
            if (Overlaps(mask, start, end))
            {
                continue;
            }

            var suffix = match.Groups["suf"].Value;
            if (suffix.Length == 0 && end < text.Length && char.IsLetter(text[end]))
            {
                continue; // glued to letters: 15m, 1D, 250d, 3x, 1st
            }

            var digits = match.Groups["num"].Value;
            if (figures && IsYear(text, match, digits, suffix, end))
            {
                continue;
            }

            tokens.Add(new NumberToken(start, end, match.Value, NumberNormaliser.Parse(digits, suffix.Trim())));
        }

        return tokens;
    }

    private static bool Overlaps(bool[] mask, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (mask[i])
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsYear(string text, Match match, string digits, string suffix, int end) =>
        suffix.Length == 0 && !match.Groups["cur"].Success && digits.Length == 4 && digits.All(char.IsAsciiDigit)
        && int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) is >= 1900 and <= 2099
        && !CountNoun().IsMatch(text.AsSpan(end));

    // Number: optional sign, optional currency, optional sign, digits (grouped, decimal, exponent), optional unit. Not preceded by a letter, digit, underscore or point.
    [GeneratedRegex(@"(?<![\p{L}\d_.])(?<sign>[-+−–])?(?<cur>(?:₹|Rs\.?|INR)\s?)?(?<sign2>[-+−–])?(?<num>\d{1,3}(?:,\d{2,3})+(?:\.\d+)?(?:[eE][-+]?\d+)?|\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)(?<suf>\s?%|\s?(?:percent|per\s?cent)\b|\s?(?:crores?|Cr)\b|\s?lakhs?\b|\s?thousand\b|[kK]\b|\s?L\b|\s?R\b)?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Candidate();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex DigitsRun();

    [GeneratedRegex(@"^\s+(?:trades?|shares?|rows?|days?|setups?|signals?|samples?|runs?|cells?|observations?|stocks?|instruments?|sessions?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CountNoun();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex IsoDates();

    [GeneratedRegex(@"\b\d{1,2}[/.]\d{1,2}[/.]\d{2,4}\b", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SlashDates();

    [GeneratedRegex(@"\b\d{1,2}(?:st|nd|rd|th)?\s+(?:" + Months + @")\b\.?(?:,?\s+\d{4})?|\b(?:" + Months + @")\b\.?\s+\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{4})?|\b(?:" + Months + @")\b\.?,?\s+\d{4}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 2000)]
    private static partial Regex TextDates();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}(?::\d{2})?(?:\s?[AaPp][Mm])?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Times();

    [GeneratedRegex(@"\b\d+\.\d+\.\d+(?:\.\d+)*", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Versions();

    // A word with a digit glued to it: ORB15, NIFTY50, NIFTY-50, v10, R2, Q3, FY2026-27, p95. "Rs1200" is a figure.
    [GeneratedRegex(@"(?<![\p{L}\d])(?!(?:Rs|INR)\d)\p{L}[\p{L}\d]*[-_/]?\d[\w\-/]*", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Identifiers();

    [GeneratedRegex(@"\[\s*\d+(?:\s*[,;–-]\s*\d+)*\s*\]", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Markers();

    [GeneratedRegex(@"#\d+|\b[Cc]alls?\s+#?\d+(?:\s*(?:and|,|&)\s*#?\d+)*", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex CallReferences();

    [GeneratedRegex(@"^[ \t]*\d{1,3}[.)](?=[ \t])", RegexOptions.CultureInvariant | RegexOptions.Multiline, 2000)]
    private static partial Regex ListNumbers();
}
