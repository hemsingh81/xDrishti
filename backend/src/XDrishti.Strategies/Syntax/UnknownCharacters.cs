using System.Globalization;
using System.Text;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// What to say about a character (or the two-character <c>&amp;&amp;</c> and <c>||</c>) that is not part of the language, and what it was most likely meant to be. A look-alike that stands for a token of the language
/// (<c>＞</c> for <c>&gt;</c>, <c>&amp;&amp;</c> for <c>and</c>) is read as that token after the problem is reported, so the mistake is one problem and not a chain of follow-on errors; a space look-alike is dropped.
/// </summary>
internal static class UnknownCharacters
{
    /// <summary>What an unknown character stands for: a token kind to read it as (<c>None</c> for nothing sensible), whether to drop it as white space, and the replacement text to suggest.</summary>
    public readonly record struct Reading(Tk Stands, bool Drop, string? Suggestion);

    /// <summary>Whether the text is a string in some other kind of quotes, as the lexer reads them as one token: <c>'abc'</c>, <c>“abc”</c>.</summary>
    public static bool IsForeignString(string text) => text.Length >= 2 && text[0] is '\'' or '\u2018' or '\u201C' or '\u201E' or '\u00AB';

    public static string Message(string text)
    {
        if (IsForeignString(text))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Unknown character '{text[0]}' ({CodePoint(text, 0)}): strings are written with straight double quotes, like \"{text[1..^1]}\".");
        }

        var points = new StringBuilder();
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            if (points.Length > 0)
            {
                points.Append(' ');
            }

            points.Append(CodePoint(text, i));
        }

        return IsPrintable(text)
            ? string.Create(CultureInfo.InvariantCulture, $"Unknown character '{text}' ({points}); it is not part of the rules language.")
            : string.Create(CultureInfo.InvariantCulture, $"Unknown character {points}; it is not part of the rules language.");
    }

    public static Reading Read(string text)
    {
        if (IsForeignString(text))
        {
            return new Reading(Tk.String, false, "\"" + text[1..^1] + "\"");
        }

        switch (text)
        {
            case "&&" or "&":
                return new Reading(Tk.And, false, "and");
            case "||" or "|":
                return new Reading(Tk.Or, false, "or");
            case "!":
                return new Reading(Tk.Not, false, "not");
        }

        if (text.Length != 1)
        {
            return new Reading(Tk.None, false, null);
        }

        var c = text[0];

        // The full-width forms of the ASCII punctuation (U+FF01 to U+FF5E: ！ … ～) stand for the ASCII character 0xFEE0 below them.
        if (c is >= '\uFF01' and <= '\uFF5E')
        {
            var ascii = (char)(c - 0xFEE0);
            return new Reading(Stands(ascii), false, ascii.ToString());
        }

        // Every character below is written as a \u escape: most of them cannot be told apart from a plain space (or from nothing at all) when they are written out, and an editor that normalises white space
        // would silently change which characters are dropped.
        return c switch
        {
            // minus sign (U+2212), en dash (U+2013), em dash (U+2014): read as "-"
            '\u2212' or '\u2013' or '\u2014' => new Reading(Tk.Minus, false, "-"),

            // multiplication sign (U+00D7), division sign (U+00F7)
            '\u00D7' => new Reading(Tk.Star, false, "*"),
            '\u00F7' => new Reading(Tk.Slash, false, "/"),

            // less-than or equal (U+2264), greater-than or equal (U+2265), not equal (U+2260)
            '\u2264' => new Reading(Tk.LessOrEqual, false, "<="),
            '\u2265' => new Reading(Tk.GreaterOrEqual, false, ">="),
            '\u2260' => new Reading(Tk.NotEqual, false, "!="),

            // spaces that are not the ASCII space, dropped with a single space as the suggestion: no-break space (U+00A0), Ogham space mark (U+1680), the en, em, thin, hair … spaces (U+2000 to U+200A),
            // narrow no-break space (U+202F), medium mathematical space (U+205F), ideographic space (U+3000)
            '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u202F' or '\u205F' or '\u3000' => new Reading(Tk.None, true, " "),

            // zero-width characters, dropped without a suggestion: zero-width space (U+200B), non-joiner (U+200C), joiner (U+200D), word joiner (U+2060), zero-width no-break space or byte-order mark (U+FEFF)
            '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF' => new Reading(Tk.None, true, null),

            // typographic double quotes (U+201C, U+201D, U+201E, U+201F) and guillemets (U+00AB, U+00BB): the suggestion is the straight double quote
            '\u201C' or '\u201D' or '\u201E' or '\u201F' or '\u00AB' or '\u00BB' => new Reading(Tk.None, false, "\""),
            _ => new Reading(Tk.None, false, null),
        };
    }

    private static Tk Stands(char ascii) => ascii switch
    {
        '(' => Tk.LeftParen,
        ')' => Tk.RightParen,
        '[' => Tk.LeftBracket,
        ']' => Tk.RightBracket,
        ',' => Tk.Comma,
        '.' => Tk.Dot,
        '<' => Tk.Less,
        '>' => Tk.Greater,
        '=' => Tk.Assign,
        '+' => Tk.Plus,
        '-' => Tk.Minus,
        '*' => Tk.Star,
        '/' => Tk.Slash,
        _ => Tk.None,
    };

    private static string CodePoint(string text, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"U+{(char.IsSurrogatePair(text, index) ? char.ConvertToUtf32(text, index) : text[index]):X4}");

    /// <summary>Whether the text can be shown in a message as itself: not a control or format character, a space, an unpaired surrogate or an unassigned code point.</summary>
    private static bool IsPrintable(string text)
    {
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            var category = char.IsSurrogatePair(text, i) ? CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(text, i)) : CharUnicodeInfo.GetUnicodeCategory(text[i]);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                return false;
            }
        }

        return true;
    }
}
