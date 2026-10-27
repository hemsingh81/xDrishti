using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The figures of an answer as the eval reads them (spec P9-09 AC 3). Written for the eval on its own, not shared with the assistant's grounding code, so that the two can disagree and be compared (AC 4).
/// A figure is a number that claims a quantity: dates, times, versions, list numbers, <c>[n]</c> markers, call references, identifiers glued to letters (<c>EVH1</c>, <c>ORB15</c>), timeframes (<c>15m</c>) and bare years are not.
/// Understands ₹, Rs, a unicode minus, Western and Indian grouping, a percent sign and the unit letters R and x. Pure.
/// </summary>
public static partial class AnswerNumbers
{
    /// <summary>The figures of <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<AnswerNumber> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mask = new bool[text.Length];
        foreach (var pattern in new[] { IsoDate(), SlashDate(), Time(), Version(), Marker(), CallReference(), ListNumber() })
        {
            foreach (var m in pattern.EnumerateMatches(text))
            {
                Array.Fill(mask, true, m.Index, m.Length);
            }
        }

        var found = new List<AnswerNumber>();
        foreach (Match match in Candidate().Matches(text))
        {
            var start = match.Index;
            var end = start + match.Length;
            if (Enumerable.Range(start, match.Length).Any(i => mask[i]))
            {
                continue;
            }

            var suffix = match.Groups["suf"].Value;
            if (suffix.Length == 0 && end < text.Length && char.IsLetter(text[end]))
            {
                continue; // 15m, 250d, 1st
            }

            var digits = match.Groups["num"].Value;
            var plain = suffix.Length == 0 && !match.Groups["cur"].Success && !digits.Contains(',', StringComparison.Ordinal) && !digits.Contains('.', StringComparison.Ordinal);
            if (plain && digits.Length == 4 && int.Parse(digits, CultureInfo.InvariantCulture) is >= 1900 and <= 2099)
            {
                continue; // a bare year
            }

            var clean = digits.Replace(",", string.Empty, StringComparison.Ordinal);
            if (!decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var point = clean.IndexOf('.', StringComparison.Ordinal);
            var decimals = point < 0 ? 0 : clean.Length - point - 1;
            if (decimals > 12)
            {
                continue;
            }

            var negative = match.Groups["sign"].Success && IsMinus(match.Groups["sign"].Value[0]) || match.Groups["sign2"].Success && IsMinus(match.Groups["sign2"].Value[0]);
            found.Add(new AnswerNumber(start, end, match.Value, negative ? -value : value, decimals, suffix.Trim() == "%"));
        }

        return found;
    }

    /// <summary>The sentences of a text as (start, end) spans: split at a line break, and after a full stop, <c>!</c>, <c>?</c> or <c>;</c> that is followed by white space.</summary>
    public static IReadOnlyList<(int Start, int End)> Sentences(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<(int, int)>();
        var start = 0;
        foreach (Match m in SentenceBreak().Matches(text))
        {
            spans.Add((start, m.Index));
            start = m.Index + m.Length;
        }

        spans.Add((start, text.Length));
        return spans;
    }

    private static bool IsMinus(char c) => c is '-' or '−' or '–';

    [GeneratedRegex(@"(?<![\p{L}\d_.,])(?<sign>[-+−–])?(?<cur>(?:₹|Rs\.?|INR)\s?)?(?<sign2>[-+−–])?(?<num>\d{1,3}(?:,\d{2,3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<suf>\s?%|R\b|x\b)?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Candidate();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b\d{1,2}[/.]\d{1,2}[/.]\d{2,4}\b", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SlashDate();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}(?::\d{2})?(?:\s?[AaPp][Mm])?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Time();

    [GeneratedRegex(@"\b\d+\.\d+\.\d+(?:\.\d+)*", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Version();

    [GeneratedRegex(@"\[\s*\d+(?:\s*[,;–-]\s*\d+)*\s*\]", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Marker();

    [GeneratedRegex(@"#\d+|\b[Cc]alls?\s+#?\d+(?:\s*(?:and|,|&)\s*#?\d+)*", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex CallReference();

    [GeneratedRegex(@"^[ \t]*\d{1,3}[.)](?=[ \t])", RegexOptions.CultureInvariant | RegexOptions.Multiline, 2000)]
    private static partial Regex ListNumber();

    [GeneratedRegex(@"[\r\n]+|(?<=[.!?;])\s+", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SentenceBreak();
}
