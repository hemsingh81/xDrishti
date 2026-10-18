using System.Globalization;
using System.Text;
using XDrishti.Application.Research.CostSheets;

namespace XDrishti.Api.CostSheets;

/// <summary>
/// The text of the bill check (spec P3-03f, F5): classes, counts, differences, the ten largest differences (date, class and difference only), the adjustments, the implied rates (a proposal, never stored) and the note it could put into the
/// sheet's <c>checked</c> field (never written). It prints no bill number, scrip name, client code, quantity or value: the report holds none of them.
/// </summary>
internal static class CostCheckText
{
    private const string Tolerance = "0.05";

    public static string Text(CostCheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        Line(text, "sheet: " + report.SheetId + " (rates " + report.RatesSha256 + ")");
        Line(text, "files read: " + Number(report.Files) + "; rows kept: " + Number(report.RowsRead) + "; rows the reader refused: " + Number(report.RowsRefused) + "; rows repeated (counted once): " + Number(report.RowsRepeated));
        Line(text, "tolerance per bill: " + Tolerance + " rupee on the total of the lines compared");
        if (report.Classes.Count == 0)
        {
            Line(text, "no row could be compared (see the skipped and unclassified lines below)");
        }

        foreach (var cls in report.Classes)
        {
            Line(text, string.Empty);
            Line(text, cls.Name + ": " + Number(cls.Rows) + " rows, " + Number(cls.Passed) + " within " + Tolerance + ", " + Number(cls.Beyond) + " beyond; largest total difference " + Rupees(cls.LargestDifference));
            foreach (var line in cls.Lines)
            {
                Line(text, "  " + line.Name + ": largest difference " + Rupees(line.LargestDifference));
            }
        }

        Skipped(text, report);
        if (report.AdjustmentRows > 0)
        {
            Line(text, string.Empty);
            Line(text, "adjustments (a negative Oth. cell, a rounding or adjustment line; the row is left out of the pass count): " + Number(report.AdjustmentRows) + " rows, sum " + Rupees(report.AdjustmentSum));
        }

        if (report.UnmodelledOtherRows > 0)
        {
            Line(text, "rows with a positive Oth. cell while the sheet's other_pct is 0 (the cell is not compared): " + Number(report.UnmodelledOtherRows));
        }

        if (report.Largest.Count > 0)
        {
            Line(text, string.Empty);
            Line(text, "largest differences (model minus bill, rupees):");
            foreach (var row in report.Largest)
            {
                Line(text, "  " + row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "  " + row.Class + "  " + Signed(row.Difference));
            }
        }

        Implied(text, report);
        Line(text, string.Empty);
        Line(text, report.SuggestedChecked is null
            ? "suggested checked note: none (" + (report.Classes.Count == 0 ? "no row was compared" : "rows are beyond the tolerance") + ")"
            : "suggested checked note (put it into the sheet's \"checked\" field and load the sheet again; this command writes nothing): " + report.SuggestedChecked);
        Line(text, string.Empty);
        Line(text, "what this cannot do: it cannot check the rates of a class that has no rows; it checks what the bills show, so a rate that was never billed is unchecked; the DP charge and account-level fees are not on this report.");
        return text.ToString();
    }

    private static void Skipped(StringBuilder text, CostCheckReport report)
    {
        if (report.Skipped.Count > 0)
        {
            Line(text, string.Empty);
            Line(text, "skipped (not NSE cash equity): " + string.Join(", ", report.Skipped.Select(s => s.Reason + " " + Number(s.Rows))));
        }

        if (report.UnclassifiedTotal == 0)
        {
            return;
        }

        Line(text, string.Empty);
        Line(text, "unclassified (fit no class, not compared): " + Number(report.UnclassifiedTotal));
        foreach (var row in report.Unclassified)
        {
            Line(text, "  " + row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + row.Reason);
        }

        if (report.UnclassifiedTotal > report.Unclassified.Count)
        {
            Line(text, "  and " + Number(report.UnclassifiedTotal - report.Unclassified.Count) + " more");
        }
    }

    private static void Implied(StringBuilder text, CostCheckReport report)
    {
        if (report.Classes.Count == 0)
        {
            return;
        }

        Line(text, string.Empty);
        Line(text, "implied rates from the bills (a proposal only; nothing is stored or turned into a sheet):");
        foreach (var cls in report.Classes)
        {
            Line(text, "  " + cls.Name + ":");
            foreach (var line in cls.Lines.Where(l => l.Implied is not null))
            {
                var implied = line.Implied!;
                var unit = line.Name == "sebi" ? " rupees per crore" : " %";
                Line(text, "    " + line.Name + ": min " + Rate(implied.Minimum) + ", median " + Rate(implied.Median) + ", max " + Rate(implied.Maximum) + unit + " over " + Number(implied.Rows) + " rows");
            }
        }

        Line(text, "  basis: a percent of the row's buy plus sell value; stt on the sell value of an intraday row; stamp duty on the buy value; gst on the brokerage, sebi and transaction cells of the row.");
        Line(text, "  limits: rounding noise at small values, no effective dates, no brokerage cap or minimum, thin intraday evidence.");
    }

    private static string Rupees(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Signed(decimal value) => (value > 0 ? "+" : string.Empty) + value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Rate(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');
}
