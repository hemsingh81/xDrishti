using System.Buffers;
using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// The shell the cards and the index share (spec P3-04c, C5): the head with its Content-Security-Policy and referrer meta, one inline stylesheet (light and dark by <c>prefers-color-scheme</c>, no <c>@import</c>, no <c>url(</c>), the banner
/// of the run (the visible labels always shown, the limits in a <c>&lt;details&gt;</c>, which needs no script) and the write to the sink. No script, no link, no image, no clock, no machine path.
/// </summary>
internal static class CardPage
{
    /// <summary>The most bytes of a page; a card is far below it, so reaching it is a defect.</summary>
    public const int MaxBytes = 150 * 1024;

    private const string Style =
        ":root{color-scheme:light dark}"
        + "body{font:14px/1.45 system-ui,sans-serif;margin:0 auto;max-width:1040px;padding:1rem;background:#fff;color:#111}"
        + "header.banner{border:1px solid currentColor;padding:.5rem 1rem;margin-bottom:1rem}"
        + "header.banner ul{margin:.25rem 0;padding-left:1.25rem}"
        + "h1{font-size:1.25rem;margin:.5rem 0}h2{font-size:1rem;margin:1.25rem 0 .25rem;border-bottom:1px solid currentColor}"
        + "table{border-collapse:collapse;margin:.25rem 0}th,td{border:1px solid #888;padding:.15rem .5rem;text-align:left;vertical-align:top}"
        + "td.n{text-align:right;font-variant-numeric:tabular-nums}"
        + "pre{white-space:pre-wrap;overflow-wrap:anywhere;margin:.25rem 0}"
        + "svg{width:100%;height:auto;font:11px sans-serif}"
        + "p.note{margin:.25rem 0}"
        + "@media (prefers-color-scheme:dark){body{background:#111;color:#eee}th,td{border-color:#666}}";

    /// <summary>The head and the opening of the body, with the banner.</summary>
    public static StringBuilder Begin(string title, RunLabelSet labels)
    {
        var page = new StringBuilder(32 * 1024);
        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n");
        page.Append("<meta name=\"referrer\" content=\"no-referrer\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        page.Append("<title>").Append(Html.Text(title)).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n");
        Banner(page, labels);
        return page;
    }

    /// <summary>Closes the body and writes the page as UTF-8 to the sink.</summary>
    public static void End(StringBuilder page, IBufferWriter<byte> sink)
    {
        page.Append("</body>\n</html>\n");
        var text = page.ToString();
        var count = Encoding.UTF8.GetByteCount(text);
        if (count > MaxBytes)
        {
            throw new InvalidOperationException("A page is " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes, above the limit of " + MaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        var span = sink.GetSpan(count);
        Encoding.UTF8.GetBytes(text, span);
        sink.Advance(count);
    }

    /// <summary>A table row of header or data cells; every text goes through <see cref="Html.Text"/>.</summary>
    public static void Row(StringBuilder page, string cellTag, params string[] cells)
    {
        page.Append("<tr>");
        foreach (var cell in cells)
        {
            page.Append('<').Append(cellTag).Append('>').Append(Html.Text(cell)).Append("</").Append(cellTag).Append('>');
        }

        page.Append("</tr>\n");
    }

    private static void Banner(StringBuilder page, RunLabelSet labels)
    {
        page.Append("<header class=\"banner\">\n<ul>\n");
        foreach (var line in labels.Lines)
        {
            page.Append("<li>").Append(Html.Text(line)).Append("</li>\n");
        }

        page.Append("</ul>\n<details>\n<summary>what the fills do not model</summary>\n<ul>\n");
        foreach (var limit in labels.NotModelled)
        {
            page.Append("<li>").Append(Html.Text("not modelled: " + limit.Code + ": " + limit.Text)).Append("</li>\n");
        }

        page.Append("</ul>\n</details>\n</header>\n");
    }
}
