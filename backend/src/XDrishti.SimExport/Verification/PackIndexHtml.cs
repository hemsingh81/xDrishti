using System.Buffers;
using System.Text;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// <c>index.html</c> of the verification pack (spec P3-09, V5): the banner of every run, the seed, a table of sequence, symbol, side and date with a relative link to the card, and the spot checks with their question. Nothing that says how a
/// trade ended or why it is in the pack: no exit reason, no source, no category, no result, no statistic. Inert and escaped by the rules of the cards (the shell and <see cref="Html"/> are theirs).
/// </summary>
public static class PackIndexHtml
{
    /// <summary>Writes the index as UTF-8 to the sink.</summary>
    public static void Write(IBufferWriter<byte> sink, string packId, string seed, IReadOnlyList<PackRun> runs, PackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(packId);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(plan);

        var title = "verification pack " + packId;
        var page = CardPage.Begin(title, [.. runs.Select(r => r.Labels)]);
        page.Append("<main>\n<h1>").Append(Html.Text(title)).Append("</h1>\n");
        page.Append("<p>seed ").Append(Html.Text(seed)).Append(", sampler ").Append(Html.Text(VerificationSampler.Version)).Append("</p>\n");
        page.Append("<p class=\"note\">Write your row of check-log.csv before you open the marks of a card or engine-values.csv. Nothing here is evidence of an edge.</p>\n");
        page.Append("<table>\n<thead>");
        CardPage.Row(page, "th", "seq", "symbol", "side", "date", "card");
        page.Append("</thead>\n<tbody>\n");
        foreach (var row in plan.Rows)
        {
            var trade = row.Trade.Trade;
            var name = PackFiles.CardName(row.Seq, plan.Rows.Count, trade);
            page.Append("<tr>");
            Cell(page, CardFormat.Int(row.Seq), "n");
            Cell(page, trade.Symbol, null);
            Cell(page, ExportSpellings.Of(trade.Side), null);
            Cell(page, CardFormat.Date(trade.TradeDate), null);
            page.Append("<td><a href=\"").Append(Html.Attr(name)).Append("\">").Append(Html.Text(name)).Append("</a></td></tr>\n");
        }

        page.Append("</tbody>\n</table>\n");
        if (plan.Spots.Count > 0)
        {
            page.Append("<h2>spot checks</h2>\n<table>\n<thead>");
            CardPage.Row(page, "th", "seq", "symbol", "date", "question");
            page.Append("</thead>\n<tbody>\n");
            foreach (var spot in plan.Spots)
            {
                page.Append("<tr>");
                Cell(page, CardFormat.Int(spot.Seq), "n");
                Cell(page, spot.Symbol, null);
                Cell(page, CardFormat.Date(spot.Date), null);
                Cell(page, spot.Question, null);
                page.Append("</tr>\n");
            }

            page.Append("</tbody>\n</table>\n");
        }

        page.Append("</main>\n");
        CardPage.End(page, sink);
    }

    private static void Cell(StringBuilder page, string text, string? css) =>
        page.Append(css is null ? "<td>" : "<td class=\"" + css + "\">").Append(Html.Text(text)).Append("</td>");
}
