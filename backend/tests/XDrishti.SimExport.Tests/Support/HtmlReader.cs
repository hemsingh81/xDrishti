using System.Text;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>One token of the strict reader: a start tag (with its attributes, in order), an end tag, a piece of text (still escaped) or the doctype.</summary>
internal abstract record HtmlToken
{
    internal sealed record Start(string Name, IReadOnlyList<(string Name, string Value)> Attributes, bool SelfClosing) : HtmlToken
    {
        public string? Attr(string name) => Attributes.Where(a => a.Name == name).Select(a => a.Value).FirstOrDefault();
    }

    internal sealed record End(string Name) : HtmlToken;

    internal sealed record Text(string Value) : HtmlToken;

    internal sealed record Doctype : HtmlToken;
}

/// <summary>
/// A strict tokenizer for the tags and attributes of our own output (spec P3-04c, AC 5): no library, because a lenient parser would repair the very mistakes the test looks for. It accepts only <c>&lt;name attr="value"&gt;</c>
/// (double quotes, a value without a raw <c>&lt;</c>, <c>&gt;</c> or <c>"</c>), <c>&lt;name .../&gt;</c>, <c>&lt;/name&gt;</c> and the doctype; a <c>&lt;</c> that starts anything else, a raw <c>&gt;</c> in text or an unterminated tag throws
/// <see cref="FormatException"/>. The contents of <c>&lt;style&gt;</c> are one text token.
/// </summary>
internal static class HtmlReader
{
    public static IReadOnlyList<HtmlToken> Read(string html)
    {
        var tokens = new List<HtmlToken>();
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                var next = html.IndexOf('<', i);
                var end = next < 0 ? html.Length : next;
                var text = html[i..end];
                if (text.Contains('>', StringComparison.Ordinal) && !InStyle(tokens))
                {
                    throw new FormatException("A raw '>' in text: " + Excerpt(text));
                }

                tokens.Add(new HtmlToken.Text(text));
                i = end;
                continue;
            }

            if (string.CompareOrdinal(html, i, "<!DOCTYPE html>", 0, 15) == 0)
            {
                tokens.Add(new HtmlToken.Doctype());
                i += 15;
                continue;
            }

            if (i + 1 < html.Length && html[i + 1] == '/')
            {
                var close = html.IndexOf('>', i);
                var name = close < 0 ? string.Empty : html[(i + 2)..close];
                if (name.Length == 0 || !name.All(char.IsAsciiLetterOrDigit))
                {
                    throw new FormatException("A bad end tag at " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                tokens.Add(new HtmlToken.End(name));
                i = close + 1;
                continue;
            }

            i = ReadStart(html, i, tokens);
        }

        return tokens;
    }

    /// <summary>The visible text of a page: the text tokens outside <c>style</c>, unescaped, joined by a single space.</summary>
    public static string VisibleText(IReadOnlyList<HtmlToken> tokens)
    {
        var text = new StringBuilder();
        var skip = false;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case HtmlToken.Start { Name: "style" }:
                    skip = true;
                    break;
                case HtmlToken.End { Name: "style" }:
                    skip = false;
                    break;
                case HtmlToken.Text t when !skip:
                    text.Append(System.Net.WebUtility.HtmlDecode(t.Value)).Append(' ');
                    break;
            }
        }

        return text.ToString();
    }

    private static bool InStyle(List<HtmlToken> tokens) => tokens.Count > 0 && tokens[^1] is HtmlToken.Start { Name: "style" };

    private static int ReadStart(string html, int start, List<HtmlToken> tokens)
    {
        var i = start + 1;
        var nameStart = i;
        while (i < html.Length && (char.IsAsciiLetterOrDigit(html[i]) || html[i] == '-'))
        {
            i++;
        }

        var name = html[nameStart..i];
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
        {
            throw new FormatException("A '<' that starts no tag at " + start.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + Excerpt(html[start..Math.Min(html.Length, start + 40)]));
        }

        var attributes = new List<(string, string)>();
        while (true)
        {
            var space = i;
            while (i < html.Length && html[i] == ' ')
            {
                i++;
            }

            if (i >= html.Length)
            {
                throw new FormatException("An unterminated tag: " + name);
            }

            if (html[i] == '>' || (html[i] == '/' && i + 1 < html.Length && html[i + 1] == '>'))
            {
                var selfClosing = html[i] == '/';
                tokens.Add(new HtmlToken.Start(name, attributes, selfClosing));
                return i + (selfClosing ? 2 : 1);
            }

            if (i == space)
            {
                throw new FormatException("An attribute without a space before it in <" + name + ">");
            }

            var attrStart = i;
            while (i < html.Length && (char.IsAsciiLetterOrDigit(html[i]) || html[i] is '-' or ':' or '_'))
            {
                i++;
            }

            var attr = html[attrStart..i];
            if (attr.Length == 0 || i + 1 >= html.Length || html[i] != '=' || html[i + 1] != '"')
            {
                throw new FormatException("An attribute that is not name=\"value\" in <" + name + ">: " + Excerpt(html[attrStart..Math.Min(html.Length, attrStart + 40)]));
            }

            var valueStart = i + 2;
            var valueEnd = html.IndexOf('"', valueStart);
            if (valueEnd < 0)
            {
                throw new FormatException("An unterminated attribute value in <" + name + ">");
            }

            var value = html[valueStart..valueEnd];
            if (value.Contains('<', StringComparison.Ordinal) || value.Contains('>', StringComparison.Ordinal))
            {
                throw new FormatException("A raw '<' or '>' in the value of " + attr + " in <" + name + ">");
            }

            attributes.Add((attr, value));
            i = valueEnd + 1;
        }
    }

    private static string Excerpt(string text) => text.Length > 60 ? text[..60] + "..." : text;
}
