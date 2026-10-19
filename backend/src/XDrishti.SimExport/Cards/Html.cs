using System.Globalization;
using System.Text;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// The one place a dynamic string becomes HTML (spec P3-04c, C5). <see cref="Text"/> and <see cref="Attr"/> first remove every character of Unicode category Control or Format (so U+202E and the other bidi controls, which
/// <c>char.IsControl</c> does not catch, cannot reorder the text, and a NUL or a zero-width space cannot hide), and every unpaired surrogate, then escape <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c>. The output is
/// UTF-8 written as it stands, so no character above U+007F is turned into an entity (the pages declare their charset). Numbers, dates and enum words come from formatters and never from here.
/// </summary>
public static class Html
{
    /// <summary>A string as the text of an element.</summary>
    public static string Text(string? value) => Escape(value);

    /// <summary>A string as the value of an attribute: the same encoding, quotes included, so the value can sit between double or single quotes.</summary>
    public static string Attr(string? value) => Escape(value);

    /// <summary>The lines of a preformatted block: each line through <see cref="Text"/> (which removes a line feed, as it removes every control character), joined by one line feed, so a line cannot add a line.</summary>
    internal static string Lines(IEnumerable<string> lines) => string.Join('\n', lines.Select(Escape));

    /// <summary>Whether a character is one that is removed: category Control or Format.</summary>
    internal static bool IsRemoved(UnicodeCategory category) => category is UnicodeCategory.Control or UnicodeCategory.Format;

    /// <summary>The string without its Control and Format characters and unpaired surrogates, not escaped (for a value that is cleaned further, such as the ticker of a label).</summary>
    internal static string Strip(string? value) => Clean(value, escape: false);

    private static string Escape(string? value) => Clean(value, escape: true);

    private static string Clean(string? value, bool escape)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                // a valid pair is kept or removed whole (a supplementary Format character is removed); a lone high surrogate is dropped
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    if (!IsRemoved(CharUnicodeInfo.GetUnicodeCategory(value, i)))
                    {
                        text.Append(c).Append(value[i + 1]);
                    }

                    i++;
                }

                continue;
            }

            if (c is '\n' or '\t' or '\r')
            {
                // control whitespace becomes a space, so that glued tokens stay apart; every other Control or Format character is removed
                text.Append(' ');
                continue;
            }

            if (char.IsLowSurrogate(c) || IsRemoved(char.GetUnicodeCategory(c)))
            {
                continue;
            }

            switch (escape ? c : '\0')
            {
                case '&':
                    text.Append("&amp;");
                    break;
                case '<':
                    text.Append("&lt;");
                    break;
                case '>':
                    text.Append("&gt;");
                    break;
                case '"':
                    text.Append("&quot;");
                    break;
                case '\'':
                    text.Append("&#39;");
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.ToString();
    }
}
