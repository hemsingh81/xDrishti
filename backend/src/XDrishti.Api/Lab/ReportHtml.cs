using System.Globalization;
using System.Text;
using XDrishti.Application.Lab;
using XDrishti.SimExport.Cards;

namespace XDrishti.Api.Lab;

/// <summary>
/// The static page of a Lab report (spec P5-06, ADR 0063): one inert HTML document, no script, link, image or frame, a Content-Security-Policy meta, every dynamic string through <see cref="Html.Text"/>. Numbers come from the invariant formatter.
/// Equal reports give equal bytes.
/// </summary>
internal static class ReportHtml
{
    /// <summary>The policy of the page, also sent as a header.</summary>
    public const string Policy = "default-src 'none'; style-src 'unsafe-inline'";

    public static string Render(LabReportView r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var h = new StringBuilder(8192);
        h.Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(Html.Attr(Policy)).Append("\">")
            .Append("<meta name=\"referrer\" content=\"no-referrer\">")
            .Append("<title>Lab report ").Append(Html.Text(r.Run.StrategyId)).Append("</title>")
            .Append("<style>body{font-family:sans-serif;max-width:60rem;margin:2rem auto;padding:0 1rem}table{border-collapse:collapse;margin:.5rem 0}td,th{border:1px solid #bbb;padding:.2rem .5rem;text-align:left}.warn{color:#8a4b00}</style>")
            .Append("</head><body>\n");
        h.Append("<h1>Lab report: ").Append(Html.Text(r.Run.StrategyId)).Append(" v").Append(r.Run.Version.ToString(CultureInfo.InvariantCulture)).Append("</h1>\n");
        h.Append("<h2>Verdict: ").Append(Html.Text(r.Outcome)).Append("</h2>\n<p>").Append(Html.Text(r.Summary)).Append("</p>\n");
        h.Append("<p>Method ").Append(Html.Text(r.Run.Method)).Append(", ").Append(Html.Text(r.Run.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(" to ")
            .Append(Html.Text(r.Run.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(", universe ").Append(Html.Text(r.Run.Universe)).Append(" (")
            .Append(r.Run.Instruments.ToString(CultureInfo.InvariantCulture)).Append(" instruments). Variants in the family: ").Append(r.VariantCount.ToString(CultureInfo.InvariantCulture))
            .Append(", threshold factor ").Append(N(r.AdjustmentFactor)).Append(".</p>\n");

        h.Append("<h2>Gates</h2>\n");
        Table(h, ["Gate", "Required", "Adjusted", "Actual", "Met", "Reason"], r.Gates.Select(g => new[] { g.Name, N(g.Required), N(g.Adjusted), N(g.Actual), g.Passed ? "yes" : "no", g.Reason }));

        var k = r.Kpis;
        h.Append("<h2>Out-of-sample figures</h2>\n");
        Table(h, ["Trades", "Expectancy R", "Profit factor", "Max drawdown R", "Folds", "Positive folds"], [[I(k.OosTrades), N(k.ExpectancyR), N(k.ProfitFactor), N(k.MaxDrawdownR), I(k.Folds), I(k.PositiveFolds)]]);

        h.Append("<h2>Best cells</h2>\n");
        Table(h, ["Cell", "Chosen exit", "Folds", "OOS trades", "OOS mean R", "Default OOS mean R", "Stable"], r.BestCells.Select(c => new[] { c.Cell, c.Chosen, I(c.Folds), I(c.OosTrades), N(c.OosMeanR), N(c.DefaultOosMeanR), c.Stable is null ? "-" : c.Stable.Value ? "yes" : "no" }));

        h.Append("<h2>Warnings</h2>\n");
        List(h, r.Warnings.Select(w => w.Code + ": " + w.Text), "warn");

        if (r.Robustness is { } b)
        {
            Robustness(h, b);
        }

        h.Append("<h2>Exploration (in-sample, not judged)</h2>\n");
        Table(h, ["Cell", "Candidate", "Trades", "Mean R", "Profit factor", "Total R", "Label"], r.Exploration.Select(f => new[] { f.Cell, f.Candidate, I(f.Trades), N(f.MeanR), N(f.ProfitFactor), N(f.TotalR), f.Label }));

        h.Append("<h2>Honest limits</h2>\n");
        List(h, r.HonestLimits, null);
        h.Append("<h2>Not in this report</h2>\n");
        List(h, r.NotIncluded, null);
        h.Append("<h2>Run</h2>\n<p>Run ").Append(Html.Text(r.RunId.ToString("D"))).Append(", strategy hash ").Append(Html.Text(r.Run.Hash)).Append(", simulation runs ")
            .Append(Html.Text(string.Join(", ", r.Run.SimRunKeys))).Append(".</p>\n");
        List(h, r.Labels, null);
        h.Append("</body></html>\n");
        return h.ToString();
    }

    private static void Robustness(StringBuilder h, RobustnessView b)
    {
        h.Append("<h2>Robustness</h2>\n<p>").Append(Html.Text(b.Sensitivity.Label)).Append("</p>\n");
        Table(h, ["Parameter", "Verdict", "Reason"], b.Sensitivity.Parameters.Select(p => new[] { p.Param, p.Verdict, p.Reason }));
        if (b.MonteCarlo is { } m)
        {
            Table(h, ["MC trades", "Draws", "Observed mean R", "Mean P05", "Mean P50", "Mean P95", "Drawdown P95", "Share not positive"], [[I(m.Trades), I(m.Draws), N(m.ObservedMeanR), N(m.MeanP05), N(m.MeanP50), N(m.MeanP95), N(m.DrawdownP95), N(m.ShareNotPositive)]]);
        }

        Table(h, ["Year", "Trades", "Mean R", "Total R", "Profit factor"], b.ByYear.Select(y => new[] { I(y.Year), I(y.Trades), N(y.MeanR), N(y.TotalR), N(y.ProfitFactor) }));
        if (b.SignFlipBaseline is { } s)
        {
            h.Append("<p>").Append(Html.Text(s.Label)).Append(" p-value ").Append(N(s.PValue)).Append(".</p>\n");
        }

        h.Append("<p>By regime: ").Append(Html.Text(b.ByRegime)).Append("</p>\n");
    }

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string N(double? value) => value is { } v && double.IsFinite(v) ? v.ToString("0.####", CultureInfo.InvariantCulture) : "-";

    private static void List(StringBuilder h, IEnumerable<string> items, string? cssClass)
    {
        var rows = items.ToList();
        if (rows.Count == 0)
        {
            h.Append("<p>None.</p>\n");
            return;
        }

        h.Append("<ul").Append(cssClass is null ? string.Empty : " class=\"" + cssClass + "\"").Append('>');
        foreach (var item in rows)
        {
            h.Append("<li>").Append(Html.Text(item)).Append("</li>");
        }

        h.Append("</ul>\n");
    }

    private static void Table(StringBuilder h, string[] head, IEnumerable<string[]> rows)
    {
        h.Append("<table><tr>");
        foreach (var c in head)
        {
            h.Append("<th>").Append(Html.Text(c)).Append("</th>");
        }

        h.Append("</tr>");
        foreach (var row in rows)
        {
            h.Append("<tr>");
            foreach (var c in row)
            {
                h.Append("<td>").Append(Html.Text(c)).Append("</td>");
            }

            h.Append("</tr>");
        }

        h.Append("</table>\n");
    }
}
