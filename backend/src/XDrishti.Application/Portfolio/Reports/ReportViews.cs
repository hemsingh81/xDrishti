using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>Turns plans and errors into the shapes of the report import API (ADR 0029). Pure.</summary>
internal static class ReportViews
{
    public const string OtherAccountCode = "portfolio.other_account";

    public const string OtherAccountText =
        "This report carries another client code than the reports imported into this account before. Choose the account it belongs to, or tick that you want it imported into this one anyway.";

    /// <summary>The most names a preview lists.</summary>
    public const int MaxListedNames = 500;

    /// <summary>The error as the API reports it per file: for a refusal that names a field, that field and its message (the message of the problem itself only says that some values are not valid).</summary>
    public static ImportFileError Error(Error error) => error.Fields is { Count: > 0 } fields
        ? new ImportFileError(error.Code, fields[0].Message, fields[0].Field)
        : new ImportFileError(error.Code, error.Message);

    public static ImportFilePreview Failed(string fileName, string kind, Error error) =>
        new(fileName, kind, false, null, null, Error(error), null, [], null, null, null, null, null);

    /// <summary>A file that is recognised but cannot be imported here: a statement of results, a tradebook with no buy/sell column, a statement of amounts.</summary>
    public static ImportFilePreview NotImported(string fileName, string kind, string message) =>
        new(fileName, kind, false, null, message, null, null, [new ImportWarning("notice_only", message)], null, null, null, null, null);

    /// <summary>A file that belongs on another screen (<c>RouteTo</c>).</summary>
    public static ImportFilePreview Routed(string fileName, string kind, string routeTo, string message) =>
        new(fileName, kind, false, routeTo, message, null, null, [], null, null, null, null, null);

    public static ImportWarning OtherAccountWarning() => new("other_account", OtherAccountText);

    public static string Side(TradeSide side) => side == TradeSide.Buy ? "buy" : "sell";

    public static BillDetails Bill(BillPlan plan)
    {
        var listed = plan.Names.Take(MaxListedNames).Select(Name).ToList();
        return new BillDetails(
            listed,
            plan.Names.Count,
            plan.Unresolved.Count(),
            plan.UnresolvedTrades,
            new BillCoverage(plan.Covered, [.. plan.Differences.Select(d => new BillDifference(d.Date, d.Isin, d.Symbol, Side(d.Side), d.BillQty, d.DhanQty))], plan.DifferencesTotal),
            new BillCharges(plan.ChargesTotal, plan.FooterCharges, !plan.ChargesDiffer));
    }

    public static BillNameView Name(BillNameItem item)
    {
        var match = item.Match;
        var security = match.Security;
        var status = match.IsResolved ? "resolved" : match.Kind == SecurityMatchKind.Ambiguous ? "ambiguous" : "needs_match";
        var kind = match.Kind == SecurityMatchKind.Ambiguous ? "none" : match.Kind.ToString().ToLowerInvariant();
        return new BillNameView(
            item.Name.Display,
            item.Name.Key,
            item.Name.SymbolHint,
            status,
            kind,
            security?.Isin,
            security?.Symbol,
            security?.Name,
            security?.HoldingId,
            security?.InstrumentId,
            item.Rows,
            item.Trades,
            [.. match.Candidates.Select(c => new BillCandidate(c.Isin, c.Symbol, c.Name, c.HoldingId, c.InstrumentId))]);
    }

    public static List<ImportWarning> BillWarnings(GtrFile file, BillPlan plan, bool otherAccount)
    {
        var warnings = new List<ImportWarning>();
        if (otherAccount)
        {
            warnings.Add(OtherAccountWarning());
        }

        if (file.BadTotal > 0)
        {
            warnings.Add(new ImportWarning("bad_rows", "Some rows cannot be read and are not imported; the first ones are listed with their line.", file.BadTotal));
        }

        var unresolved = plan.Unresolved.Count();
        if (unresolved > 0)
        {
            warnings.Add(new ImportWarning("needs_match", "Some scrip names cannot be tied to one security. Their trades are imported without an ISIN and do not join a holding; choose what each name means first (the choice is remembered).", unresolved));
        }

        if (plan.Partial > 0)
        {
            warnings.Add(new ImportWarning("partial_name_match", "Some names were matched by a spelling that differs in the ending of a word. Check them.", plan.Partial));
        }

        if (plan.ChargesDiffer)
        {
            warnings.Add(new ImportWarning("charges_differ_from_footer", "The charges of the rows do not add up to the report's own total charges (within ₹0.01)."));
        }

        if (plan.NegativeChargeRows > 0)
        {
            warnings.Add(new ImportWarning("negative_charges", "Some rows have charges that are a net refund; they are imported with no charges.", plan.NegativeChargeRows));
        }

        if (plan.InexactValues > 0)
        {
            warnings.Add(new ImportWarning("value_not_reproduced", "For some trades quantity × price does not give the bill's value to the paisa (the price is kept to 6 decimals).", plan.InexactValues));
        }

        if (plan.Covered > 0)
        {
            warnings.Add(new ImportWarning("covered_by_dhan", "Trades that Dhan's own trade history has for the same day, ISIN and side are left out: Dhan's execution-level trades replace the report's bill rows.", plan.Covered));
        }

        if (plan.DifferencesTotal > 0)
        {
            warnings.Add(new ImportWarning("totals_differ", "For some days the total quantity of Dhan's trades differs from the report's. Dhan's are used.", plan.DifferencesTotal));
        }

        return warnings;
    }
}
