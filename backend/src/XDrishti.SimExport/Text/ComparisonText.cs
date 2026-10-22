using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;

namespace XDrishti.SimExport.Text;

/// <summary>
/// The text of a comparison for the console and the readme (spec P4-05, D6, D7, D11): the banner lines a comparison adds, the table on each candidate's own trades and on the common set, and the MAE/MFE table with the censor level beside each
/// figure. It prints the figures of the report and no sentence of its own that reads one: a reading sentence is the report's, and absent below 30 trades or beyond a censor level.
/// </summary>
public static class ComparisonText
{
    /// <summary>The banner lines of a comparison (D11), one per entry; the gate line only for a swing strategy.</summary>
    /// <param name="swing">The strategy holds positions overnight.</param>
    public static IReadOnlyList<string> BannerLines(bool swing)
    {
        var lines = new List<string>
        {
            "candidates compared on the same entries; the best row is in-sample on the data shown and is not evidence of an edge (P4-00 criterion 9)",
            "censor levels: a figure that asks for more than the walk itself lets it show (a share reaching 3R under a 2R target, a dip beyond 1R under a stop at 1R) is censored, printed as such and read in no sentence",
        };
        if (swing)
        {
            lines.Add("gate not applied in the comparison (swing): every candidate walks every entry, overlaps allowed, so the number of simultaneous positions is overstated; the entries a gated run would have skipped are counted for each candidate");
        }

        return lines.AsReadOnly();
    }

    /// <summary>The table of D6: one block on each candidate's own trades, one on the common set with the difference from the first candidate.</summary>
    public static string Table(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.Append("comparison (unit: ").Append(report.Unit).Append("; open-at-end trades are not counted; entries taken under every candidate: ").Append(report.CommonEntries.ToString(CultureInfo.InvariantCulture)).Append(")\n");
        text.Append("each candidate's own trades\n");
        Grid(text, report.Rows.Select(r => Cells(r.Id, r.Own, [r.NotCommon.ToString(CultureInfo.InvariantCulture), r.GatedBlocked.ToString(CultureInfo.InvariantCulture)])), ["candidate", "trades", "wins", "win rate", "mean R", "median R", "down dev", "total R", "profit factor", "hold min", "not common", "gated"]);
        text.Append("the common set (the paired comparison)\n");
        Grid(text, report.Rows.Select(r => Cells(r.Id, r.Common, [r.Paired is null ? "-" : r.Paired.MeanDifferenceR is { } d ? ComparisonFormat.Fixed(d) : "n/a", r.Paired is null ? "-" : r.Paired.Entries.ToString(CultureInfo.InvariantCulture)])), ["candidate", "trades", "wins", "win rate", "mean R", "median R", "down dev", "total R", "profit factor", "hold min", "diff from first", "entries"]);
        return text.ToString();
    }

    /// <summary>The MAE/MFE table of D7 and D8: each figure with its sample and its censor level; a reading sentence only where it is allowed.</summary>
    public static string Diagnostics(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.Append("MAE/MFE diagnostics (excursions are measured on the path the walk took, so the walk's own exit censors them)\n");
        foreach (var cell in report.Diagnostics)
        {
            text.Append(cell.Id).Append(": ").Append(cell.Trades.ToString(CultureInfo.InvariantCulture)).Append(" trades").Append(cell.BelowMinimum ? " (n < " + ComparisonReport.MinimumTrades.ToString(CultureInfo.InvariantCulture) + ", no reading)" : string.Empty).Append('\n');
            foreach (var figure in cell.Figures)
            {
                text.Append("  ").Append(figure.Name).Append(": ")
                    .Append(figure.Value is { } v ? (figure.IsShare ? ComparisonFormat.Percent(v) + " (" + figure.Count.ToString(CultureInfo.InvariantCulture) + " of " + figure.Of.ToString(CultureInfo.InvariantCulture) + ")" : ComparisonFormat.Fixed(v) + " (n " + figure.Of.ToString(CultureInfo.InvariantCulture) + ")") : "n/a")
                    .Append("; censor level: ").Append(figure.Censor).Append(figure.Censored ? "; CENSORED, beyond the level" : string.Empty);
                if (figure.Reading is { } reading)
                {
                    text.Append("; ").Append(reading);
                }

                text.Append('\n');
            }
        }

        return text.ToString();
    }

    private static string[] Cells(string id, SampleFigures s, string[] tail) =>
    [
        id,
        s.Trades.ToString(CultureInfo.InvariantCulture),
        s.Wins.ToString(CultureInfo.InvariantCulture),
        Value(s.WinRate),
        Value(s.MeanR),
        Value(s.MedianR),
        Value(s.DownsideDeviation),
        Value(s.TotalR),
        Value(s.ProfitFactor),
        Value(s.AverageHoldMinutes),
        .. tail,
    ];

    private static string Value(double? value) => value is { } v ? ComparisonFormat.Fixed(v) : "n/a";

    private static void Grid(StringBuilder text, IEnumerable<string[]> rows, string[] header)
    {
        var all = new List<string[]> { header };
        all.AddRange(rows);
        var widths = Enumerable.Range(0, header.Length).Select(c => all.Max(r => r[c].Length)).ToArray();
        foreach (var row in all)
        {
            text.Append("  ");
            for (var c = 0; c < row.Length; c++)
            {
                text.Append(c == 0 ? row[c].PadRight(widths[c]) : row[c].PadLeft(widths[c]));
                text.Append(c == row.Length - 1 ? '\n' : "  ");
            }
        }
    }
}
