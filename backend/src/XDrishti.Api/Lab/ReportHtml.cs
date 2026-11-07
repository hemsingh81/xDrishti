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
        if (r.DeflatedSharpe is null)
        {
            h.Append("<h2>Deflated Sharpe ratio</h2>\n<p>Deflated Sharpe ratio: not computed.</p>\n");
        }
        else if (r.DeflatedSharpe is { } d)
        {
            h.Append("<h2>Deflated Sharpe ratio</h2>\n");
            Table(h, ["Trials", "Ledger trials", "Trial SR sd", "Luck bar SR", "SR", "T", "Probability", "Read against"], [[d.Trials is { } n ? I(n) : "-", I(d.LedgerTrials), N(d.TrialSrSd), N(d.LuckBarSr), N(d.Sr), N(d.NEff), N(d.Probability), N(d.MinProbability)]]);
            h.Append("<p>").Append(Html.Text(d.Note)).Append("</p>\n");
        }

        if (r.CostStress is { } c)
        {
            h.Append("<h2>Costs and delay</h2>\n<p>Mode ").Append(Html.Text(c.Mode)).Append(c.Enforce ? ", enforced" : ", report only (the outcome is not changed)").Append("; tradable: ").Append(Html.Text(c.Tradable)).Append(c.CostsChecked ? "." : "; costs not checked against a bill: stress gates cannot PASS.").Append("</p>\n");
            Table(h, ["Multiplier", "Trades", "Net expectancy R", "Floor R", "Comparison", "Status", "Reason"], c.Rows.Select(x => new[] { N(x.Multiplier) + "x", I(x.Trades), N(x.ExpectancyR), N(x.Threshold), x.Comparison, x.Status, x.Reason }));
            if (c.Delay is { } d)
            {
                Table(h, ["Delay minutes", "Signals", "Still filled", "Base mean R per signal", "Delayed mean R per signal", "Kept", "Needs", "Status", "Reason"], [[I(d.Minutes), I(d.Signals), I(d.DelayedFilled), N(d.BaseMeanR), N(d.DelayedMeanR), N(d.Retention), N(d.Required), d.Status, d.Reason]]);
            }

            h.Append("<p>").Append(Html.Text(c.Limit)).Append(" Formula: ").Append(Html.Text(c.Formula)).Append(". Settings ").Append(Html.Text(c.Settings)).Append(" (").Append(Html.Text(c.SettingsHash)).Append(").</p>\n");
        }

        if (r.Nulls is { } nulls)
        {
            var e = nulls.RandomEntry;
            h.Append("<h2>Chance: random entry</h2>\n<p>Mode ").Append(Html.Text(nulls.Mode)).Append(nulls.Enforce ? ", enforced" : ", report only (the outcome is not changed)").Append("; status: ").Append(Html.Text(e.Status)).Append(". ").Append(Html.Text(e.Reason)).Append(".</p>\n");
            Table(h, ["Trades", "Seeds", "Real mean R", "Random mean R", "P5", "P50", "P95", "P99", "At or above real", "p-value", "Threshold", "Status", "Dropped share", "Seed"], [[I(e.Trades) + " of " + I(e.OosTrades), I(e.ValidSeeds) + " of " + I(e.Draws), N(e.RealMeanR), N(e.RandomMeanR), N(e.P05), N(e.P50), N(e.P95), N(e.P99), I(e.CountAtLeast), N(e.PValue), e.Comparison + " " + N(e.Threshold), e.Status, N(e.DroppedShare), e.Seed]]);
            if (nulls.Placebos is { } set)
            {
                h.Append("<h2>Chance: placebo set</h2>\n<p>Status: ").Append(Html.Text(set.Status)).Append(". ").Append(Html.Text(set.Reason)).Append(".</p>\n");
                Table(h, ["Rank of the strategy", "Comparable placebos", "Needed", "Real mean R", "p-value", "Threshold", "Status"], [[set.RealRank is { } rank ? I(rank) + " of " + I(set.Comparable + 1) : "-", I(set.Comparable) + " of " + I(set.Shapes), I(set.MinComparable), N(set.RealMeanR), N(set.PValue), set.Comparison + " " + N(set.Threshold), set.Status]]);
                Table(h, ["Shape", "Trades", "Mean R", "Rank", "Note"], [.. set.Rows.Select(row => new[] { row.Shape, I(row.Trades), N(row.MeanR), row.Rank is { } at ? I(at) : "-", row.Reason ?? string.Empty })]);
            }

            if (nulls.Mirror is { } mirror)
            {
                h.Append("<h2>Chance: mirror</h2>\n<p>Status: ").Append(Html.Text(mirror.Status)).Append(". ").Append(Html.Text(mirror.Reason)).Append(".</p>\n");
                Table(h, ["Trades", "Mirror mean R", "Real mean R", "Threshold", "Status"], [[I(mirror.Trades), N(mirror.MirrorMeanR), N(mirror.RealMeanR), mirror.Comparison + " " + N(mirror.Threshold), mirror.Status]]);
            }

            if (nulls.Plateau is { } plateau)
            {
                h.Append("<h2>Chance: plateau (in-sample sweep)</h2>\n<p>Mode ").Append(Html.Text(plateau.Mode)).Append(plateau.Enforce ? ", enforced" : ", report only (the outcome is not changed)").Append("; status: ").Append(Html.Text(plateau.Status)).Append(". ").Append(Html.Text(plateau.Reason)).Append(".</p>\n");
                Table(h, ["Parameter", "Declared", "Valid points", "Base mean R", "Below", "Keeps", "Above", "Keeps", "Needs", "Median R", "Plateau centre", "Reading", "Status"], [.. plateau.Parameters.Select(x => new[] { x.Param, N(x.Declared), I(x.ValidPoints), N(x.BaseMeanR), N(x.LowerValue), N(x.LowerRetention), N(x.UpperValue), N(x.UpperRetention), plateau.Comparison + " " + N(plateau.Retention), N(x.Median), x.CentreValue is { } cv ? N(cv) + " (smoothed " + N(x.CentreMeanR) + " R)" : "-", x.Reading, x.Status })]);
                foreach (var x in plateau.Parameters)
                {
                    h.Append("<p>").Append(Html.Text(x.Param)).Append(": ").Append(Html.Text(x.Reason)).Append(".</p>\n");
                    Table(h, ["Value", "Declared", "Trades", "Mean R", "Kept of base"], [.. x.Points.Select(pt => new[] { N(pt.Value), pt.Declared ? "yes" : "no", I(pt.Trades), N(pt.MeanR), N(pt.Retention) })]);
                }

                h.Append("<p>").Append(Html.Text(plateau.Basis)).Append(". Settings ").Append(Html.Text(plateau.Settings)).Append(" (").Append(Html.Text(plateau.SettingsHash)).Append(").</p>\n");
            }

            h.Append("<p>").Append(Html.Text(nulls.Basis)).Append(". Settings ").Append(Html.Text(nulls.Settings)).Append(" (").Append(Html.Text(nulls.SettingsHash)).Append(").</p>\n");
        }

        if (r.Trials is { } trials)
        {
            h.Append("<h2>Looks at this strategy</h2>\n<p>").Append(Html.Text(trials.Text)).Append("</p>\n");
        }

        if (r.Registration is { } reg)
        {
            h.Append("<h2>Pre-registration</h2>\n<p>State: ").Append(Html.Text(reg.State)).Append(". ").Append(Html.Text(reg.Text)).Append("</p>\n");
            if (reg.Registration is { } text)
            {
                Table(h, ["Part", "Text"], [["Who is on the other side and why they lose", text.Counterparty], ["Expected sign of each parameter", text.ParameterSigns], ["Tiers and sessions", text.Applicability], ["Expected gross edge and the cost to beat", text.ExpectedEdge], ["What would retire it", text.Falsifier]]);
            }
        }

        if (r.HoldOut is { } hold)
        {
            h.Append("<h2>Hold-out</h2>\n<p>State: ").Append(Html.Text(hold.State)).Append(". ").Append(Html.Text(hold.Text)).Append("</p>\n");
            if (hold.Result is { } look)
            {
                Table(h, ["Mode", "Window", "Trades", "Hold-out R", "Development R", "Kept", "Status", "Counts toward a pass", "Reason"], [[look.Mode, look.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " to " + look.To.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), I(look.Trades), N(look.HoldOutExpectancyR), N(look.DevelopmentExpectancyR), N(look.Ratio), look.Status, look.Passed ? "yes" : "no", look.Reason]]);
            }
        }

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
            Table(h, ["MC trades", "Draws", "Observed mean R", "Mean P05", "Mean P50", "Mean P95", "Drawdown P90", "Drawdown P95", "Share not positive"], [[I(m.Trades), I(m.Draws), N(m.ObservedMeanR), N(m.MeanP05), N(m.MeanP50), N(m.MeanP95), N(m.DrawdownP90), N(m.DrawdownP95), N(m.ShareNotPositive)]]);
        }

        if (b.MonteCarloBlock is null)
        {
            h.Append("<p>Block bootstrap: not computed.</p>\n");
        }
        else if (b.MonteCarloBlock is { } k)
        {
            Table(h, ["Block sessions", "Block length", "Draws", "Block mean P05", "Block mean P50", "Block mean P95", "Block drawdown P95", "Share not positive", "Width over iid"], [[I(k.Sessions), k.BlockLength is { } l ? I(l) : "-", I(k.Draws), N(k.P05), N(k.P50), N(k.P95), N(k.DdP95), N(k.ShareNonPositive), N(k.WidthRatioToIid)]]);
            h.Append("<p>").Append(Html.Text(k.Note)).Append("</p>\n");
        }

        Table(h, ["Year", "Trades", "Mean R", "Total R", "Profit factor"], b.ByYear.Select(y => new[] { I(y.Year), I(y.Trades), N(y.MeanR), N(y.TotalR), N(y.ProfitFactor) }));
        if (b.ByQuarter is { Count: > 0 } quarters)
        {
            Table(h, ["Quarter", "Trades", "Mean R"], quarters.Select(q => new[] { I(q.Year) + " Q" + I(q.Quarter), I(q.Trades), N(q.MeanR) }));
        }

        if (b.MaxInstrumentShare is { } share)
        {
            h.Append("<p>Largest instrument share of the total R: ").Append(N(share)).Append(".</p>\n");
        }

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
