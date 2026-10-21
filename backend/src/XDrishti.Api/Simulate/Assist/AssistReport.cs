using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>
/// <c>assist-report.html</c> (spec P3-09c D6, D7): a static page. It says first that it is not a signed check, prints the hashes of the readings (written before the engine's values were read) and of the engine values, counts the rows per verdict (counts only, no percentage),
/// lists the rows with their bar differences and reader differences, and names the rows the owner should read on a real chart, picked by a fixed rule from the seed. No time, no machine fact: the same inputs give the same bytes.
/// </summary>
internal static class AssistReport
{
    public const int ChartRows = 5;

    public static string Html(string packId, string seed, string secondSource, IReadOnlyList<RowVerdict> verdicts, string logHash, string engineHash, string barsHash)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        var page = new StringBuilder();
        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Assisted check ").Append(Enc(packId)).Append("</title>")
            .Append("<style>body{font:14px system-ui,sans-serif;margin:24px}table{border-collapse:collapse}td,th{border:1px solid #bbb;padding:3px 8px;vertical-align:top;text-align:left}.warn{border:2px solid #b00;padding:8px}</style></head><body>\n");
        page.Append("<h1>Assisted check of pack ").Append(Enc(packId)).Append("</h1>\n");
        page.Append("<p class=\"warn\"><b>This is not a signed check.</b> A machine read every trade on a second source (").Append(Enc(secondSource)).Append("); no verdict of the owner is written, check-log.csv is untouched, ")
            .Append("and a row read here is not one of the rows against an external chart. A reader written by the same model family can share a misreading of the rule tables. ")
            .Append("<b>agree_all means that the engine's trade equals an independent reading on both the stored bars and the second source's bars; it does not mean that the two data sources agree.</b> reader_differs is the reader on the stored bars (the data the engine used) disagreeing with the engine: a rules or implementation candidate. data_sensitive is the reader agreeing on the stored bars but not on the second source: the trade depends on the feed, a data finding for the owner. Gaps between the two sources are a data note per row; only a gross problem (a missing minute, a price level outside the band or not constant, a gap above ")
            .Append(BarComparison.GrossGapPercent.ToString("0.##", CultureInfo.InvariantCulture)).Append(" % of price) makes a row bars_differ.</p>\n");
        page.Append("<h2>Evidence</h2><ul>")
            .Append("<li>assist-log.csv sha256 (written before engine-values.csv was read): <code>").Append(logHash).Append("</code></li>")
            .Append("<li>engine-values.csv sha256 (read after): <code>").Append(engineHash).Append("</code></li>")
            .Append("<li>second-source-bars.csv sha256: <code>").Append(barsHash).Append("</code></li></ul>\n");
        page.Append("<h2>Rows per verdict</h2><ul>");
        foreach (var verdict in Verdicts.All)
        {
            page.Append("<li>").Append(verdict).Append(": ").Append(verdicts.Count(v => v.Verdict == verdict).ToString(CultureInfo.InvariantCulture)).Append("</li>");
        }

        var all = verdicts.SelectMany(v => v.Row.Bars).ToList();
        page.Append("<li>").Append(all.Count(b => b.IsNoise).ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(all.Count.ToString(CultureInfo.InvariantCulture)).Append(" compared minutes differ from the second source beyond a match (data notes; a feed that is off everywhere shows here)</li>");
        foreach (var line in AssistCounters.Lines(verdicts))
        {
            page.Append("<li>").Append(line).Append("</li>");
        }

        var maxima = verdicts.Select(v => RowMax(v.Row.Bars)).Where(m => m is not null).Select(m => m!.Value).Order().ToList();
        var noted = verdicts.Count(v => v.Row.Bars.Any(b => !b.Matches) || v.Row.OffsetNote is not null);
        page.Append("<li>").Append(noted.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(verdicts.Count.ToString(CultureInfo.InvariantCulture)).Append(" rows carry a data note; median of the row-maximum gaps ")
            .Append(maxima.Count == 0 ? "none" : Percent(Median(maxima))).Append(", the largest ").Append(maxima.Count == 0 ? "none" : Percent(maxima[^1])).Append(" of price</li>");
        page.Append("<li>").Append(verdicts.Count(v => v.Row.OffsetNote?.StartsWith("price level offset", StringComparison.Ordinal) == true).ToString(CultureInfo.InvariantCulture)).Append(" rows carry a price level offset note (stored prices are a constant ratio of the second source's; adjusted history?)</li>");
        page.Append("</ul>\n<p>Rows with a price level note (read their level on a raw chart, with the adjustment setting in mind): ")
            .Append(string.Join(", ", verdicts.Where(v => v.Row.OffsetNote is not null).Select(v => v.Row.Pack.Seq.ToString(CultureInfo.InvariantCulture)).DefaultIfEmpty("none"))).Append(".</p>\n<p>Rows whose largest gap reaches ").Append(Percent(BarComparison.NoteFlagPercent)).Append(" of price (a data note, not a verdict; read them on a real chart): ")
            .Append(string.Join(", ", verdicts.Where(v => RowMax(v.Row.Bars) >= BarComparison.NoteFlagPercent).Select(v => v.Row.Pack.Seq.ToString(CultureInfo.InvariantCulture)).DefaultIfEmpty("none"))).Append(".</p>\n<h2>Rows to read on a real chart</h2><p>Every row that is not agree_all (reader_differs, cannot_read, bars_differ, data_sensitive and source_unavailable: the last has no second reading, but the owner still has to check that trade on a chart), and agree_all rows picked by a fixed rule (SHA-256 of the seed and the trade key, smallest first; at least one Sell if there is one): ")
            .Append(string.Join(", ", Sample(seed, verdicts).Select(s => s.ToString(CultureInfo.InvariantCulture)).DefaultIfEmpty("none"))).Append(".</p>\n");
        page.Append("<h2>Rows</h2><table><tr><th>seq</th><th>trade</th><th>verdict</th><th>source</th><th>reader on stored differs on</th><th>reader on second source differs on</th><th>bar comparison (data note: minutes compared, how many differ, median and max gap; then the minutes that are not a match)</th></tr>\n");
        foreach (var v in verdicts)
        {
            var notes = v.Row.Unavailable ?? (v.Row.Reading is null ? v.Row.SecondNote ?? "the reader found no trade on the second source" : string.Empty);
            var bars = string.Join("; ", v.Row.Bars.Where(b => !b.Matches).Select(b => b.Minute + " " + b.Words));
            page.Append("<tr><td>").Append(v.Row.Pack.Seq.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(Enc(v.Row.Pack.TradeKey)).Append("</td><td>").Append(v.Verdict).Append("</td><td>").Append(Enc(v.Row.Source)).Append("</td><td>")
                .Append(Enc(string.Join(", ", v.Fields.Append(v.Row.StoredNote ?? string.Empty).Where(n => n.Length > 0)))).Append("</td><td>")
                .Append(Enc(string.Join(", ", v.SecondFields.Append(notes).Where(n => n.Length > 0)))).Append("</td><td>").Append(Enc(Size(v.Row.Bars) + (v.Row.OffsetNote is null ? string.Empty : "; " + v.Row.OffsetNote) + (v.Row.TickFallback ? "; tick unknown: fallback used" : string.Empty) + (bars.Length == 0 ? "; all minutes match" : "; " + bars))).Append("</td></tr>\n");
        }

        page.Append("</table>\n<h2>Conventions the rows used</h2><p>Entry limit +0.1 % of the trigger close, filled at the next minute that traded; a gap fills at the open; the order inside a minute follows ADR 0038; prices exact, no slippage. ")
            .Append("The owner reads these: only the owner can say whether a convention is what is meant.</p>\n</body></html>\n");
        return page.ToString();
    }

    private static decimal? RowMax(IReadOnlyList<MinuteComparison> bars)
    {
        var all = bars.Where(b => b.Percent is not null).Select(b => b.Percent!.Value).ToList();
        return all.Count == 0 ? null : all.Max();
    }

    private static string Percent(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture) + " %";

    private static decimal Median(List<decimal> sorted) => sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2m;

    private static string Size(IReadOnlyList<MinuteComparison> bars)
    {
        var paired = bars.Where(b => b.Percent is not null).ToList();
        if (paired.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bars.Count} compared, none paired");
        }

        var ticks = paired.Select(b => (decimal)b.Ticks).Order().ToList();
        var percents = paired.Select(b => b.Percent!.Value).Order().ToList();
        return string.Create(CultureInfo.InvariantCulture, $"{bars.Count} compared, {paired.Count(b => b.Ticks > 0)} differ, gap median {BarComparison.Plural((int)Math.Ceiling(Median(ticks)))} and max {BarComparison.Plural((int)ticks[^1])}, median {Percent(Median(percents))} and max {Percent(percents[^1])} of price");
    }

    /// <summary>The seqs to read on a real chart: every row that is not <c>agree_all</c> plus agree_all rows by a fixed rule, up to <see cref="ChartRows"/> rows in all (at least one agree_all, and a Sell when an agree_all Sell exists).</summary>
    public static IReadOnlyList<int> Sample(string seed, IReadOnlyList<RowVerdict> verdicts)
    {
        var differ = verdicts.Where(v => v.Verdict != Verdicts.AgreeAll).Select(v => v.Row.Pack.Seq).ToList();
        var agree = verdicts.Where(v => v.Verdict == Verdicts.AgreeAll)
            .OrderBy(v => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + v.Row.Pack.TradeKey))), StringComparer.Ordinal).ToList();
        var take = Math.Min(agree.Count, Math.Max(1, ChartRows - differ.Count));
        var picked = agree.Take(take).ToList();
        if (picked.Count > 0 && picked.All(v => v.Row.Pack.Side != ReaderSide.Sell) && agree.FirstOrDefault(v => v.Row.Pack.Side == ReaderSide.Sell) is { } sell)
        {
            picked[^1] = sell;
        }

        return [.. differ.Concat(picked.Select(v => v.Row.Pack.Seq)).Order()];
    }

    private static string Enc(string text) => WebUtility.HtmlEncode(text);
}
