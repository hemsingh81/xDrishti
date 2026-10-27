using System.Globalization;
using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// A second reader of the numbers of a briefing text, written on its own (a character scan, not the regular expressions of the assistant's tokenizer) so that it can catch what the guard lets through (spec P9-09 AC 5, "escape").
/// It lists every figure of the text that is not a figure of the facts: the same printed value, or the fact's value rounded half away from zero to the decimals the text printed. Dates, clock times, timeframes and words with a digit glued to them
/// are not figures; a bare year must occur in the facts. Sign and unit are not compared (the guard does that); this audit asks only "is this number in the facts at all". Pure.
/// </summary>
public static class IndependentNumberAudit
{
    /// <summary>The printed figures of <paramref name="text"/> that no fact holds, in order.</summary>
    public static IReadOnlyList<string> Absent(string text, BriefingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(facts);
        var known = Scan(facts.AllText());
        var everything = facts.AllText();
        var absent = new List<string>();
        foreach (var figure in Scan(text))
        {
            if (figure.IsYear)
            {
                if (!everything.Contains(figure.Printed, StringComparison.Ordinal))
                {
                    absent.Add(figure.Printed);
                }

                continue;
            }

            if (!known.Any(k => !k.IsYear && Same(k, figure)))
            {
                absent.Add(figure.Printed);
            }
        }

        return absent;
    }

    private static bool Same(Figure fact, Figure printed)
    {
        if (printed.Decimals >= fact.Decimals)
        {
            return printed.Value == fact.Value;
        }

        return Math.Round(fact.Value, printed.Decimals, MidpointRounding.AwayFromZero) == printed.Value;
    }

    private static List<Figure> Scan(string text)
    {
        var found = new List<Figure>();
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsAsciiDigit(text[i]) || Glued(text, i))
            {
                i = SkipWord(text, i);
                continue;
            }

            var start = i;
            var digits = new System.Text.StringBuilder();
            var decimals = 0;
            var seenPoint = false;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsAsciiDigit(c))
                {
                    digits.Append(c);
                    decimals += seenPoint ? 1 : 0;
                }
                else if (c == ',' && !seenPoint && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
                {
                    // a grouping comma (1,234 or 2,49,93,750)
                }
                else if (c == '.' && !seenPoint && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
                {
                    seenPoint = true;
                    digits.Append('.');
                }
                else
                {
                    break;
                }

                i++;
            }

            if (IsDateOrTime(text, start, i) || (i < text.Length && char.IsLetter(text[i]) && !IsUnitLetter(text, i)))
            {
                continue;
            }

            if (decimal.TryParse(digits.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                var printed = text[start..i];
                var year = !seenPoint && !printed.Contains(',', StringComparison.Ordinal) && printed.Length == 4 && value is >= 1900 and <= 2099;
                found.Add(new Figure(printed, value, decimals, year));
            }
        }

        return found;
    }

    // A digit that belongs to a word (EV12, pnl1250) or to a longer number already skipped.
    private static bool Glued(string text, int index) => index > 0 && (char.IsLetter(text[index - 1]) || text[index - 1] == '_' || text[index - 1] == '.' && index > 1 && char.IsAsciiDigit(text[index - 2]));

    private static bool IsUnitLetter(string text, int index) => text[index] is 'R' or 'x' && (index + 1 == text.Length || !char.IsLetter(text[index + 1]));

    private static int SkipWord(string text, int index)
    {
        var i = index;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        return i == index ? i + 1 : i;
    }

    // 2026-03-03, 03-03 and 09:15 are dates and times, not figures.
    private static bool IsDateOrTime(string text, int start, int end) =>
        (end + 1 < text.Length && text[end] is '-' or ':' && char.IsAsciiDigit(text[end + 1]))
        || (start >= 2 && text[start - 1] is '-' or ':' && char.IsAsciiDigit(text[start - 2]));

    private sealed record Figure(string Printed, decimal Value, int Decimals, bool IsYear);
}
