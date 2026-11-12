using System.Globalization;
using System.Text;
using XDrishti.Application.Research.CostSheets;

namespace XDrishti.Api.CostSheets;

/// <summary>
/// The printout of <c>cost-sheet derive</c> (spec CS-01): where the files went, the derived rates (a line without bills is <c>unavailable</c>), unstable lines with their date range, the share of bills reproduced within a rupee on the bills the rates
/// came from and on those they did not, the ten largest residuals, and the verdict. It prints no bill number, scrip name, client code, quantity or value.
/// </summary>
internal static class CostDeriveText
{
    public static string Text(CostDerivation d, string path, string residualsPath)
    {
        ArgumentNullException.ThrowIfNull(d);
        var text = new StringBuilder();
        Line(text, "sheet written (mode 0600): " + path);
        Line(text, "residuals written (mode 0600): " + residualsPath);
        Line(text, "files read: " + N(d.Files) + "; rows kept: " + N(d.RowsRead) + "; rows the reader refused: " + N(d.RowsRefused) + "; rows repeated (counted once): " + N(d.RowsRepeated) + "; rows not used (no class, or an adjustment): " + N(d.Unclassified));
        if (d.OneSidedWithBrokerage > 0)
        {
            Line(text, "one-sided NSE bills with brokerage above 0 (not used; delivery brokerage cannot be taken as 0): " + N(d.OneSidedWithBrokerage));
        }

        foreach (var skip in d.Skipped)
        {
            Line(text, "skipped " + skip.Reason + ": " + N(skip.Rows) + " rows");
        }

        Line(text, "rates come from " + N(d.DeriveSet.Bills) + " bills, " + Date(d.From) + " to " + Date(d.To) + "; " + N(d.CheckSet.Bills) + " newer bills are kept for the check");
        Line(text, "no database row was written; loading and confirming the sheet stay your acts (cost-sheet load, then Settings, Charge sheet)");
        Line(text, string.Empty);
        Line(text, "derived rates (median of the implied rate of each bill; min and max show the spread):");
        foreach (var line in d.Lines)
        {
            Line(text, "  " + line.Class + " " + line.Line + ": " + Rate(line));
            if (line.Unstable)
            {
                Line(text, "    unstable: the rate moved by more than 5 % of its median between " + Date(line.From!.Value) + " and " + Date(line.To!.Value) + "; a budget change needs your effective date");
            }
        }

        if (d.Lines.Any(l => l.Rate is null))
        {
            Line(text, "  unavailable lines are null in the file: fill them from your tariff before loading (the loader refuses a null).");
        }

        if (d.Lines.Any(l => l.Line == "sebi" && l.Rate is null))
        {
            Line(text, "  sebi may often be unavailable on small accounts: its cell is a few paise, so rounding alone moves the implied rate by more than 5 %; fill it from your tariff.");
        }

        foreach (var note in d.Assumed)
        {
            Line(text, "assumed, not derived: " + note);
        }

        Line(text, string.Empty);
        Line(text, "reproduced within 1.00 rupee of the billed total (the sheet's modelled total against the bill):");
        Set(text, d.DeriveSet);
        Set(text, d.CheckSet);
        if (d.Largest.Count > 0)
        {
            Line(text, string.Empty);
            Line(text, "largest residuals (date, class, set, billed, modelled, difference; all bills are in the residuals file):");
            foreach (var r in d.Largest)
            {
                Line(text, "  " + Date(r.Date) + "  " + r.Class + "  " + r.Set + "  " + Rupees(r.Billed) + "  " + Rupees(r.Modelled) + "  " + Signed(r.Difference));
            }
        }

        Line(text, string.Empty);
        Line(text, d.DeriveSet.Passes && d.CheckSet.Passes
            ? "verdict: MEETS the rule (at least 95 % of bills within 1 rupee on both sets). The sheet is still unchecked: read it, then load it."
            : "verdict: DOES NOT MEET the rule (at least 95 % of bills within 1 rupee on both sets"
                + (d.CheckSet.Bills == 0 ? "; there are no newer bills to check on" : string.Empty)
                + "). Do not rely on this sheet as it is.");
        if (d.Lines.Any(l => l.Rate is null))
        {
            Line(text, "the file has null (unavailable) lines: it cannot be loaded until you fill them from your tariff, and the residuals above price a null line as 0.");
        }

        return text.ToString();
    }

    private static void Set(StringBuilder text, DerivedSet set) =>
        Line(text, "  " + set.Name + " set: " + N(set.Within) + " of " + N(set.Bills) + " bills" + (set.Bills == 0 ? " (none)" : " = " + (set.Share * 100m).ToString("0.0", CultureInfo.InvariantCulture) + " %") + (set.Passes ? ", meets the rule" : ", does not meet the rule"));

    private static string Rate(DerivedLine line)
    {
        if (line.Rate is null)
        {
            return "unavailable: " + (line.Note ?? "no bill has this line above 0") + "; fill from your tariff";
        }

        var unit = line.Line == "sebi" ? " rupees per crore" : " %";
        if (line.Implied is null)
        {
            return R(line.Rate.Value) + (line.Line == "dp charge" ? " rupees (assumed: no DP charge on an intraday bill)" : line.Line == "brokerage cap" ? " rupees per leg (" + line.Note + ")" : " (every one-sided NSE bill has this cell at 0)");
        }

        return R(line.Rate.Value) + unit + " (min " + R(line.Implied.Minimum) + ", max " + R(line.Implied.Maximum) + ", over " + N(line.Implied.Rows) + " bills)";
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Rupees(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Signed(decimal value) => (value > 0 ? "+" : string.Empty) + Rupees(value);

    private static string R(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');
}
