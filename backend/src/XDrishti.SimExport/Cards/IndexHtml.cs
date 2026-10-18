using System.Buffers;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// <c>index.html</c> of the cards (spec P3-04c, C6): the banner of the run, the cap, the number of trades of the run and a table of the cards (number, symbol, side, date, exit reason, the exact result of the card's own trade) with a relative
/// link to each. It prints no size of a stratum, no count by exit reason or side, no percentage and no figure over several trades. It has the only <c>href</c> of the pages, and every value of it is a name from <see cref="CardNames"/>.
/// </summary>
public static class IndexHtml
{
    /// <summary>Writes the index as UTF-8 to the sink. The table is empty when the run has no trade that gets a card.</summary>
    /// <param name="sink">Where the bytes go.</param>
    /// <param name="run">The run.</param>
    /// <param name="labels">The labels of the run.</param>
    /// <param name="cap">The most cards that were asked for (<c>--cards</c>).</param>
    /// <param name="choices">The cards, in the run's trade order.</param>
    public static void Write(IBufferWriter<byte> sink, SimulationRun run, RunLabelSet labels, int cap, IReadOnlyList<CardChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(choices);

        var title = "trade cards of " + run.Strategy.Id + " " + CardFormat.Date(run.From) + " to " + CardFormat.Date(run.To);
        var page = CardPage.Begin(title, labels);
        page.Append("<main>\n<h1>").Append(Html.Text(title)).Append("</h1>\n");
        page.Append("<p>cards asked for: up to ").Append(Html.Text(CardFormat.Int(cap))).Append("; the run has ").Append(Html.Text(CardFormat.Int(run.Trades.Count))).Append(" trades; cards written: ").Append(Html.Text(CardFormat.Int(choices.Count))).Append("</p>\n");
        page.Append("<p class=\"note\">The cards are an early look at what the rule does, one trade each. They are chosen by a seeded order (the run key) so that every kind of outcome is shown, which is not a picture of how often each happens. Run key ")
            .Append(Html.Text(run.RunKey)).Append("</p>\n");
        page.Append("<table>\n<thead>");
        CardPage.Row(page, "th", "number", "symbol", "side", "date", "exit reason", "exact result R", "card");
        page.Append("</thead>\n<tbody>\n");
        foreach (var choice in choices)
        {
            var trade = choice.Trade.Trade;
            var name = CardNames.Of(choice.Number, run.Trades.Count, trade);
            page.Append("<tr>");
            Cell(page, CardFormat.Int(choice.Number), "n");
            Cell(page, trade.Symbol, null);
            Cell(page, ExportSpellings.Of(trade.Side), null);
            Cell(page, CardFormat.Date(trade.TradeDate), null);
            Cell(page, ExportSpellings.Of(trade.ExitReason), null);
            Cell(page, CardFormat.SignedR(trade.RExact), "n");
            page.Append("<td><a href=\"").Append(Html.Attr(name)).Append("\">").Append(Html.Text(name)).Append("</a></td></tr>\n");
        }

        page.Append("</tbody>\n</table>\n</main>\n");
        CardPage.End(page, sink);
    }

    private static void Cell(System.Text.StringBuilder page, string text, string? css) =>
        page.Append(css is null ? "<td>" : "<td class=\"" + css + "\">").Append(Html.Text(text)).Append("</td>");
}
