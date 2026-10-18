using System.Text.RegularExpressions;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>
/// The scan of C5 over the tokens of a page: only whitelisted elements and attributes, no URL-bearing attribute, no <c>href</c> but the index's (matching the pattern), no value that starts with <c>javascript:</c> or <c>data:</c>, no
/// <c>on…</c> attribute, no <c>url(</c> or <c>@import</c> in a <c>style</c> attribute or element, and the Content-Security-Policy meta present. The text outside tags is not scanned for <c>http</c> or <c>//</c>: it is escaped text.
/// </summary>
internal static partial class CardScan
{
    private static readonly HashSet<string> Elements =
    [
        "html", "head", "meta", "title", "style", "body", "header", "main", "section", "h1", "h2", "p", "ul", "li", "details", "summary", "table", "thead", "tbody", "tr", "th", "td", "pre", "svg", "g", "line", "rect", "text", "a",
    ];

    private static readonly HashSet<string> Attributes =
    [
        "lang", "charset", "http-equiv", "content", "name", "class", "viewBox", "role", "aria-label", "x", "y", "x1", "x2", "y1", "y2", "width", "height", "fill", "stroke", "stroke-width", "stroke-dasharray", "text-anchor", "href",
    ];

    private static readonly string[] UrlAttributes = ["src", "srcset", "action", "formaction", "poster", "xlink:href", "data", "background", "ping"];

    [GeneratedRegex(@"^cards/[A-Za-z0-9._&-]+\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexHref();

    public const string ExpectedCsp = "default-src 'none'; style-src 'unsafe-inline'";

    /// <summary>The problems found on the page; empty when it is clean.</summary>
    public static IReadOnlyList<string> Problems(string html, bool isIndex)
    {
        var problems = new List<string>();
        IReadOnlyList<HtmlToken> tokens;
        try
        {
            tokens = HtmlReader.Read(html);
        }
        catch (FormatException bad)
        {
            return ["not readable by the strict reader: " + bad.Message];
        }

        var hasCsp = false;
        var inStyle = false;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case HtmlToken.Start start:
                    Check(start, isIndex, problems);
                    hasCsp |= start.Name == "meta" && start.Attr("http-equiv") == "Content-Security-Policy" && start.Attr("content") == ExpectedCsp;
                    inStyle = start.Name == "style";
                    break;
                case HtmlToken.End:
                    inStyle = false;
                    break;
                case HtmlToken.Text text when inStyle && (text.Value.Contains("url(", StringComparison.OrdinalIgnoreCase) || text.Value.Contains("@import", StringComparison.OrdinalIgnoreCase)):
                    problems.Add("url( or @import in the style element");
                    break;
            }
        }

        if (!hasCsp)
        {
            problems.Add("the Content-Security-Policy meta is missing");
        }

        return problems;
    }

    private static void Check(HtmlToken.Start start, bool isIndex, List<string> problems)
    {
        if (!Elements.Contains(start.Name))
        {
            problems.Add("element <" + start.Name + "> is not whitelisted");
        }

        foreach (var (name, value) in start.Attributes)
        {
            if (!Attributes.Contains(name))
            {
                problems.Add("attribute " + name + " on <" + start.Name + "> is not whitelisted");
            }

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || UrlAttributes.Contains(name, StringComparer.OrdinalIgnoreCase) || name == "style")
            {
                problems.Add("attribute " + name + " is forbidden");
            }

            var trimmed = value.TrimStart();
            if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("attribute " + name + " starts with a script or data scheme");
            }

            if (value.Contains("url(", StringComparison.OrdinalIgnoreCase) || value.Contains("@import", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("attribute " + name + " has url( or @import");
            }

            if (name == "href" && !(isIndex && start.Name == "a" && IndexHref().IsMatch(value)))
            {
                problems.Add("href '" + value + "' is not a relative card link of the index");
            }
        }
    }
}
